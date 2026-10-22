using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media.Imaging;
using Core.Events;
using Core.Halcon.Helpers;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Plugin.ResultDisplay;
using Plugin.ResultDisplay.Services;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「结果显示」插件的契约断言（A1~A9 + C1~C5 + U1~U9 文字位置 + 15 条静态扫描）。
    ///
    /// 【为什么分三层】三层各证一件事，缺任何一层都会留下"看着对其实错"的口子：
    ///   · **plan 级**（A3/A4/A5）离线、确定性、不碰 HALCON 窗口 —— 证"结果项翻译成了正确的图形描述"。
    ///     这一层最值钱：行主口径 (dr,dc)=(sin φ,cos φ) 写反了，图形看着"差不多直"、量全错，
    ///     只有把 P[] 摊开来比数字才抓得住（用 φ=30 这种不对称角，φ=0/45/90 都分不出来）。
    ///   · **像素级**（C2 + 椭圆角符号）真烘焙真采样 —— 证"描述真的烧进了像素"。plan 对不代表画对：
    ///     HALCON 的 disp_ellipse 角度符号与本仓 GeometryParam 差一个负号，φ=0/90 看不出来，φ=45 才现形。
    ///   · **端到端**（C5）真加载 .vms → 真编译 → 真执行 —— 证"手工写的方案文件真能跑"。
    ///     `$type` 白名单 / 端口名 / 连线类型 / 动态端口重建时机这四个坑只有真编译才现形。
    ///
    /// 【为什么像素采样必须逐通道】<c>dump_window_image</c> 出的是**三通道**图，而几何固定用 cyan=(0,255,255)：
    /// 红通道恒为 0。照搬 <c>AnnotationBakerChecks.ToMono</c>（只读第 1 通道）会把"画了 cyan"读成"什么都没画"，
    /// 断言假绿。所以本文件的采样一律走 <see cref="BandMax"/> / <see cref="ChannelMax"/> / <see cref="HasCyan"/>。
    /// </summary>
    internal static class ResultDisplayCheck
    {
        public static void Run()
        {
            PlanLevel();
            PublishAndPixels();
            TextPosition();
            ConfigLifecycle();
            NeverStopLine();
            ThumbnailAndExport();
            StaticScans();
            DemoSolution();
        }

        // ==================================================================
        //  A3 / A4 / A5：结果项 → BakePlan 的翻译（离线、确定性、不碰 HALCON 窗口）
        // ==================================================================
        private static void PlanLevel()
        {
            Section("[RD-A3/A4/A5] 结果显示：结果项 → BakePlan 的翻译（plan 级，确定性）");

            const int W = 640, H = 480;

            // ---- A3：行主口径。GeometryParam.Phi = atan2(dr, dc) ⇒ 方向向量 = (sin φ, cos φ) ----
            {
                var res = Build(GeometryParam.Line(200, 300, 0), W, H);
                var line = res.Plan.Shapes.FirstOrDefault(s => s.Kind == BakeShapeKind.Line);
                Check("A3 φ=0 → **水平**线（P[0]==P[2] 且 P[1]!=P[3]）",
                    line.P is { Length: 4 } && Eq(line.P[0], line.P[2]) && !Eq(line.P[1], line.P[3]),
                    line.P == null ? "(plan 里没有 Line)" : "P=[" + Fmt(line.P) + "]");
                res.DisposeTemps();
            }
            {
                var res = Build(GeometryParam.Line(200, 300, 90), W, H);
                var line = res.Plan.Shapes.FirstOrDefault(s => s.Kind == BakeShapeKind.Line);
                Check("A3 φ=90 → **竖直**线（P[1]==P[3] 且 P[0]!=P[2]）",
                    line.P is { Length: 4 } && Eq(line.P[1], line.P[3]) && !Eq(line.P[0], line.P[2]),
                    line.P == null ? "(plan 里没有 Line)" : "P=[" + Fmt(line.P) + "]");
                res.DisposeTemps();
            }
            {
                // φ=30 才是"行主 vs 列主"的判别角：半长 = min(640,480)/3 = 160（两端各 160 ⇒ 全长 320）
                //   行主 (sin,cos) → Δrow = 320·sin30 = 160，Δcol = 320·cos30 = 277.13
                //   列主 (cos,sin) → 两者对调
                var res = Build(GeometryParam.Line(200, 300, 30), W, H);
                var line = res.Plan.Shapes.FirstOrDefault(s => s.Kind == BakeShapeKind.Line);
                double dRow = line.P == null ? double.NaN : line.P[2] - line.P[0];
                double dCol = line.P == null ? double.NaN : line.P[3] - line.P[1];
                Check("A3【行主口径】φ=30 → Δrow=160、Δcol=277.13（列主写法会把两者对调）",
                    line.P is { Length: 4 } && Eq(dRow, 160.0, 0.01) && Eq(dCol, 320.0 * Math.Cos(30 * Math.PI / 180), 0.01),
                    line.P == null ? "(plan 里没有 Line)"
                        : "Δrow=" + dRow.ToString("0.###", CultureInfo.InvariantCulture)
                          + " Δcol=" + dCol.ToString("0.###", CultureInfo.InvariantCulture));
                res.DisposeTemps();
            }

            // ---- A4：各 Kind → 正确的 BakeShapeKind 与 P 长度 ----
            {
                var res = Build(GeometryParam.Point(100, 200), W, H);
                var cross = res.Plan.Shapes.FirstOrDefault(s => s.Kind == BakeShapeKind.Cross);
                Check("A4 Point → Cross，P 长 2 或 3（共享件两种都认），中心就是给定点",
                    cross.P is { Length: >= 2 and <= 3 } && Eq(cross.P[0], 100) && Eq(cross.P[1], 200),
                    cross.P == null ? "(没有 Cross)" : "P=[" + Fmt(cross.P) + "]");
                res.DisposeTemps();
            }
            {
                var res = Build(GeometryParam.Line(200, 300, 0), W, H);
                Check("A4 Line → BakeShapeKind.Line 且 P 长 4",
                    res.Plan.Shapes.Any(s => s.Kind == BakeShapeKind.Line && s.P.Length == 4),
                    "shapes=[" + string.Join(",", res.Plan.Shapes.Select(s => s.Kind + ":" + s.P.Length)) + "]");
                res.DisposeTemps();
            }
            {
                var res = Build(GeometryParam.Circle(240, 320, 100), W, H);
                var circle = res.Plan.Shapes.FirstOrDefault(s => s.Kind == BakeShapeKind.Circle);
                Check("A4 Circle → BakeShapeKind.Circle 且 P 长 3（row,col,radius）+ 一个圆心十字",
                    circle.P is { Length: 3 } && Eq(circle.P[2], 100)
                    && res.Plan.Shapes.Any(s => s.Kind == BakeShapeKind.Cross),
                    circle.P == null ? "(没有 Circle)" : "P=[" + Fmt(circle.P) + "]");
                res.DisposeTemps();
            }
            {
                // ★ 角度符号：本仓 (sin φ, cos φ) vs HALCON (−sin Phi, cos Phi) ⇒ plan 里必须是 **−Φ**
                var res = Build(GeometryParam.Ellipse(240, 320, 45, 100, 30), W, H);
                var e = res.Plan.Shapes.FirstOrDefault(s => s.Kind == BakeShapeKind.Ellipse);
                Check("A4 Ellipse → P 长 5（row,col,**phi 度**,ra,rb）且 P[2] == −Φ（角度取反）",
                    e.P is { Length: 5 } && Eq(e.P[2], -45) && Eq(e.P[3], 100) && Eq(e.P[4], 30),
                    e.P == null ? "(没有 Ellipse)" : "P=[" + Fmt(e.P) + "]");
                res.DisposeTemps();
            }
            {
                HOperatorSet.GenRectangle1(out HObject hr, 50, 60, 150, 260);
                using var region = new HRegion(hr);
                var res = Build(region, W, H);
                bool sameHandle = res.Plan.Objects.Count == 1 && ReferenceEquals(res.Plan.Objects[0].Obj, region);
                Check("A4 HRegion → 进 **Objects**（不是 Shapes；判型顺序 HImage→GeometryParam→HRegion→HXLD→HObject）",
                    res.Plan.Objects.Count == 1 && res.Plan.Shapes.Count == 0,
                    "objects=" + res.Plan.Objects.Count + " shapes=" + res.Plan.Shapes.Count);
                Check("A4 上游区域的所有权不被接管：不进 TempObjects，plan 里是**同一句柄**",
                    res.TempObjects.Count == 0 && sameHandle,
                    "temps=" + res.TempObjects.Count + " 同句柄=" + sameHandle);
                res.DisposeTemps();
            }
            {
                HOperatorSet.GenContourPolygonXld(out HObject xo,
                    new HTuple(new double[] { 50, 60, 70 }), new HTuple(new double[] { 50, 120, 190 }));
                using var xld = new HXLD(xo);
                var res = Build(xld, W, H);
                Check("A4 HXLD → 进 **Objects**（不是 Shapes）",
                    res.Plan.Objects.Count == 1 && res.Plan.Shapes.Count == 0,
                    "objects=" + res.Plan.Objects.Count + " shapes=" + res.Plan.Shapes.Count);
                res.DisposeTemps();
            }
            {
                // Contour 是"本插件自己造的 XLD" → 必须登记进 TempObjects，由调用方 Bake 之后释放
                var res = Build(GeometryParam.Contour(new double[] { 50, 50, 60, 120, 70, 190, 80, 60 }), W, H);
                Check("A4 Contour → 生成一条 XLD 进 Objects，且登记进 TempObjects（用完必须释放）",
                    res.Plan.Objects.Count == 1 && res.TempObjects.Count == 1,
                    "objects=" + res.Plan.Objects.Count + " temps=" + res.TempObjects.Count);
                res.DisposeTemps();

                var shortOne = Build(GeometryParam.Contour(new double[] { 50, 50 }), W, H);
                Check("A4 Contour 点数不足（<4 个坐标）→ 跳过 + 告警，不抛",
                    shortOne.Plan.Objects.Count == 0 && shortOne.Warnings.Any(w => w.Contains("点数不足")),
                    "objects=" + shortOne.Plan.Objects.Count + " warns=[" + string.Join(" | ", shortOne.Warnings) + "]");
                shortOne.DisposeTemps();
            }

            // ---- A5：非有限值 / 超大坐标 / 色名白名单 / 元素上限 ----
            {
                var res = Build(GeometryParam.Circle(double.NaN, 300, 50), W, H);
                Check("A5 NaN 坐标 → 一个图形都不进 plan（**不占预算**），且有告警留痕",
                    res.Plan.Shapes.Count == 0 && res.Warnings.Any(w => w.Contains("非有限值")),
                    "shapes=" + res.Plan.Shapes.Count + " warns=[" + string.Join(" | ", res.Warnings) + "]");
                res.DisposeTemps();

                var inf = Build(GeometryParam.Circle(double.PositiveInfinity, 300, 50), W, H);
                Check("A5 +Inf 坐标 → 同样不进 plan",
                    inf.Plan.Shapes.Count == 0 && inf.Warnings.Any(w => w.Contains("非有限值")),
                    "shapes=" + inf.Plan.Shapes.Count);
                inf.DisposeTemps();
            }
            {
                var res = Build(GeometryParam.Circle(1e9, 1e9, 1e9), W, H);
                var c = res.Plan.Shapes.FirstOrDefault(s => s.Kind == BakeShapeKind.Circle);
                Check("A5 超大坐标 → **坐标分量**钳进图幅，半径原样保留（钳半径是篡改数据不是保护）",
                    c.P is { Length: 3 }
                    && c.P[0] >= 0 && c.P[0] <= H - 1 && c.P[1] >= 0 && c.P[1] <= W - 1
                    && Eq(c.P[2], 1e9),
                    c.P == null ? "(没有 Circle)" : "P=[" + Fmt(c.P) + "] 图幅=" + W + "x" + H);
                res.DisposeTemps();
            }
            {
                // 一次 Build 里把四种色都逼出来：几何 cyan、判定 OK green、判定 NG red
                var green = BuildMixed(W, H, "判定", "NG", "OK");
                var red = BuildMixed(W, H, "判定", "NG", "NG");
                var colors = green.Plan.Shapes.Select(s => s.Color)
                    .Concat(green.Plan.Texts.Select(t => t.Color))
                    .Concat(red.Plan.Shapes.Select(s => s.Color))
                    .Concat(red.Plan.Texts.Select(t => t.Color))
                    .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                Check("A5 plan 里的色名全在共享件白名单内（绝不把用户字符串当 HALCON 色名传下去）",
                    colors.Count > 0 && colors.All(AllowedColors.Contains), string.Join(",", colors));
                Check("A5 判定项命中 NG 值 → 文本转 red；不命中 → 全不是 red；几何恒 cyan（不参与判定换色）",
                    red.Plan.Texts.Any(t => t.Color == "red") && green.Plan.Texts.All(t => t.Color != "red")
                    && green.Plan.Shapes.All(s => s.Color == "cyan"),
                    "NG文本色=[" + string.Join(",", red.Plan.Texts.Select(t => t.Color).Distinct())
                    + "] OK文本色=[" + string.Join(",", green.Plan.Texts.Select(t => t.Color).Distinct()) + "]");
                green.DisposeTemps();
                red.DisposeTemps();
            }
            {
                // 元素上限：一个 Point = 1 Cross + 1 文字 = 2 个元素。预算 10 ⇒ 画满 5 项，
                // 第 6 项起领不到名额 → Truncated，末尾追加一条**不占预算**的截断说明
                var defs = new List<DisplayItem>();
                for (int i = 1; i <= 20; i++)
                    defs.Add(new DisplayItem(
                        new DisplayItemDef { Id = "r" + i.ToString(CultureInfo.InvariantCulture), Label = "点" + i.ToString(CultureInfo.InvariantCulture), Enabled = true },
                        GeometryParam.Point(30 + i * 8, 300)));

                var res = DisplayDraw.Build(defs, W, H, true, "", "NG", 10);
                string lastText = res.Plan.Texts.Count > 0 ? res.Plan.Texts[res.Plan.Texts.Count - 1].Text : "(无)";
                Check("A5 元素数超上限 → plan 里只有预算内的图形（5 个 Cross）",
                    res.Plan.Shapes.Count == 5, "shapes=" + res.Plan.Shapes.Count + "（期望 5）");
                Check("A5 超上限 → 5 行标签 + 1 条截断说明（说明行**不占**预算，否则这件事本身说不出来）",
                    res.Plan.Texts.Count == 6 && lastText.Contains("仅绘制前"),
                    "texts=" + res.Plan.Texts.Count + " 末行='" + lastText + "'");
                Check("A5 DisplayedCount 只数**真画进去**的项（5，不是 20）",
                    res.DisplayedCount == 5, res.DisplayedCount.ToString(CultureInfo.InvariantCulture));
                res.DisposeTemps();
            }
            {
                // ShowTextOnImage=false → 只烧图形不烧字，但 infoRows 照传（悬停仍能追溯）
                var res = Build(GeometryParam.Circle(240, 320, 100), W, H, false);
                Check("A5 关掉图上叠字 → plan 里零文字，但图形照画、infoRows 照给",
                    res.Plan.Texts.Count == 0 && res.Plan.Shapes.Count == 2 && res.InfoRows.Count == 1,
                    "texts=" + res.Plan.Texts.Count + " shapes=" + res.Plan.Shapes.Count + " infoRows=" + res.InfoRows.Count);
                res.DisposeTemps();
            }
        }

        // ==================================================================
        //  A1 / A2 / C1 / C2 + 椭圆角符号：真执行、真发布、真采样像素
        // ==================================================================
        private static void PublishAndPixels()
        {
            Section("[RD-A1/A2/C1/C2] 结果显示：发布语义（viewIndex 不加不减）与像素级烧录");

            var events = new List<ImageDisplayEvent<HImage>>();
            Action<ImageDisplayEvent<HImage>> sub = e => events.Add(e);
            GlobalEventBus.Subscribe(sub);
            try
            {
                // ---- A1：事件里的 ViewIndex == 配置值，不加不减 ----
                {
                    events.Clear();
                    using var img = BlackCanvas(320, 240);
                    var p = NewPlugin(3, img, It("r1", 12.34));
                    var (ctx, log) = NewContext();
                    bool ret = p.Execute(ctx);
                    string viewIndexes = string.Join(",", events.Select(e => e.ViewIndex.ToString(CultureInfo.InvariantCulture)));
                    Check("A1 事件里的 ViewIndex == 配置值 3（**不加不减**；写过 +1 导致'选窗口1图进第2格'）",
                        events.Count == 1 && events[0].ViewIndex == 3,
                        events.Count == 0 ? "(零事件) warns=[" + string.Join(" | ", log.Warns) + "]" : "ViewIndex=[" + viewIndexes + "]");
                    Check("A1 Execute 返回 true 且 IsDisplayed=true、DisplayedCount=1",
                        ret && p.IsDisplayed.Value is true && p.DisplayedCount.TypedValue == 1,
                        "ret=" + ret + " IsDisplayed=" + p.IsDisplayed.Value + " DisplayedCount=" + p.DisplayedCount.TypedValue);
                    Check("A1 事件 Title == 「结果显示」、PluginName == 步骤实例名",
                        events.Count == 1 && events[0].Title == "结果显示" && events[0].PluginName == "结果显示断言",
                        events.Count == 0 ? "(零事件)" : "Title='" + events[0].Title + "' Plugin='" + events[0].PluginName + "'");
                    Check("A1【D10 只烧录不双发】annotations 一律为空（同一批标注既烧像素又走覆盖层 = 画上两遍）",
                        events.Count == 1 && (events[0].Annotations == null || events[0].Annotations.Count == 0),
                        events.Count == 0 ? "(零事件)" : "annotations=" + (events[0].Annotations?.Count ?? -1));
                    p.Dispose();
                }

                // ---- A2：DisplayViewIndex=0 → 零事件但产物照算；=12 → 夹到 9 ----
                {
                    events.Clear();
                    using var img = BlackCanvas(320, 240);
                    var p = NewPlugin(0, img, It("r1", 7.5));
                    var (ctx, log) = NewContext();
                    p.Execute(ctx);
                    Check("A2 DisplayViewIndex=0 → **零事件**（不抢占窗口）",
                        events.Count == 0, "事件数=" + events.Count + " warns=[" + string.Join(" | ", log.Warns) + "]");
                    Check("A2 DisplayViewIndex=0 → IsDisplayed=false，但 AnnotatedImage 照算（下游 CSV记录 仍能存带标注的图）",
                        p.IsDisplayed.Value is false && p.AnnotatedImage.TypedValue != null && p.AnnotatedImage.TypedValue.IsInitialized(),
                        "IsDisplayed=" + p.IsDisplayed.Value + " 产物=" + (p.AnnotatedImage.TypedValue != null));
                    p.Dispose();
                }
                {
                    events.Clear();
                    using var img = BlackCanvas(320, 240);
                    var p = NewPlugin(12, img, It("r1", 7.5));
                    Check("A2 配置钩子把 12 夹到 9（>9 收得进图集但界面上没有格子显示）",
                        p.DisplayViewIndex == 9, p.DisplayViewIndex.ToString(CultureInfo.InvariantCulture));
                    var (ctx, _) = NewContext();
                    p.Execute(ctx);
                    Check("A2 事件里的 ViewIndex ≤ 9",
                        events.Count == 1 && events[0].ViewIndex <= 9,
                        events.Count == 0 ? "(零事件)" : "ViewIndex=" + events[0].ViewIndex);
                    p.Dispose();
                }

                // ---- C1：产物是新图（不是底图透传），且发布的就是产物 ----
                {
                    events.Clear();
                    using var img = BlackCanvas(320, 240);
                    var p = NewPlugin(1, img, It("r1", GeometryParam.Circle(120, 160, 60)));
                    var (ctx, log) = NewContext();
                    p.Execute(ctx);
                    var src = p.SrcImage.ActualValue;
                    var product = p.AnnotatedImage.TypedValue;
                    bool same = ReferenceEquals(product, src);
                    Check("C1 产物与底图**不是同一句柄**，且两者都 IsInitialized（D12：烧录产出的是全新图）",
                        events.Count == 1 && product != null && src != null && !same
                        && product.IsInitialized() && src.IsInitialized(),
                        "同句柄=" + same + " 产物=" + (product != null && product.IsInitialized())
                        + " 底图=" + (src != null && src.IsInitialized()));
                    Check("C1 发布的就是产物本身（event.Image 与 AnnotatedImage.Value 同句柄）",
                        events.Count == 1 && ReferenceEquals(events[0].Image, product),
                        events.Count == 0 ? "(零事件)" : "同句柄=" + ReferenceEquals(events[0].Image, product));
                    Check("C1【禁透传口】底图句柄在烧录后仍然可用（没有被插件顺手释放）",
                        SizeOf(src) == (320, 240), SizeOf(src) + " ｜ warns=[" + string.Join(" | ", log.Warns) + "]");
                    p.Dispose();
                }

                // ---- C2：像素级 —— 纯黑底图 + cyan 圆 → 圆周命中 cyan、远处仍是黑 ----
                {
                    const int W = 640, H = 480;
                    using var img = BlackCanvas(W, H);
                    var p = NewPlugin(0, img, It("r1", GeometryParam.Circle(240, 320, 100)));
                    var (ctx, log) = NewContext();
                    p.Execute(ctx);
                    var product = p.AnnotatedImage.TypedValue;
                    Check("C2 纯黑底图 + 一个圆 → 产出非 null 且尺寸不变",
                        product != null && SizeOf(product) == (W, H),
                        product == null ? "(null) warns=[" + string.Join(" | ", log.Warns) + "]" : SizeOf(product).ToString());
                    if (product != null)
                    {
                        HOperatorSet.CountChannels(product, out HTuple chn);
                        Check("C2 前置：产物确实是三通道（dump_window_image 的口径，逐通道采样的前提）",
                            chn.I == 3, "channels=" + chn.I);

                        // 圆顶点 = (240−100, 320) = (140, 320)
                        double gOn = ChannelMax(product, 2, 135, 315, 145, 325);
                        double rOn = ChannelMax(product, 1, 135, 315, 145, 325);
                        Check("C2【真的烧进去了】圆周采样点命中 cyan（绿通道 ≥200 且红通道 ≤60）",
                            gOn >= 200 && rOn <= 60, "绿=" + gOn + " 红=" + rOn);

                        double far = BandMax(product, 10, 10, 40, 90);
                        Check("C2 远离圆周处仍是黑底（max ≤40，没有莫名笔画）",
                            far <= 40, "band(10..40, 10..90) max=" + far);

                        double cross = BandMax(product, 234, 314, 246, 326);
                        Check("C2 圆心十字也烧进去了（max ≥200）", cross >= 200,
                            "band(234..246,314..326) max=" + cross);
                    }
                    p.Dispose();
                }

                // ---- 椭圆角符号：φ=45 才现形的镜像陷阱（A4 只证 plan 里是 −45，这里证画对了）----
                {
                    const int W = 640, H = 480;
                    using var img = BlackCanvas(W, H);
                    var p = NewPlugin(0, img, It("r1", GeometryParam.Ellipse(240, 320, 45, 100, 30)));
                    var (ctx, log) = NewContext();
                    p.Execute(ctx);
                    var product = p.AnnotatedImage.TypedValue;
                    Check("椭圆角符号：产出非 null", product != null,
                        product == null ? "(null) warns=[" + string.Join(" | ", log.Warns) + "]" : "");
                    if (product != null)
                    {
                        // 行主口径 (dr,dc)=(sin45,cos45) ⇒ 长轴端点 = (240+70.7, 320+70.7) = (310.7, 390.7)
                        // 角度符号写反 ⇒ 镜像到 (240−70.7, 320+70.7) = (169.3, 390.7)
                        double right = BandMax(product, 305, 385, 316, 396);
                        double mirror = BandMax(product, 163, 385, 174, 396);
                        Check("椭圆角符号【φ=45 镜像陷阱】长轴端点画在 (310.7,390.7)（max ≥150）",
                            right >= 150, "band(305..316, 385..396) max=" + right);
                        Check("椭圆角符号【φ=45 镜像陷阱】镜像位置 (169.3,390.7) 是黑的（max ≤40）",
                            mirror <= 40, "band(163..174, 385..396) max=" + mirror);
                    }
                    p.Dispose();
                }

                // ---- 文字白底框真的烧进像素（Box:true 的堆叠行）----
                {
                    const int W = 400, H = 300;
                    using var img = BlackCanvas(W, H);
                    var p = NewPlugin(0, img, It("r1", 42.0, "F2", "mm"));
                    var (ctx, log) = NewContext();
                    p.Execute(ctx);
                    var product = p.AnnotatedImage.TypedValue;
                    // 排版：Margin=max(12, 300*0.02=6)=12；LineHeight=max(20,10.5)=20；FontSize=clamp(8,14,64)=14
                    double box = product == null ? -1 : BandMax(product, 8, 8, 40, 200);
                    Check("文字项带白底框（左上角第一行采到接近白的像素，max ≥240）",
                        box >= 240, "band(8..40, 8..200) max=" + box + " ｜ warns=[" + string.Join(" | ", log.Warns) + "]");
                    double outside = product == null ? -1 : BandMax(product, 200, 10, 290, 390);
                    Check("没写字的地方仍是黑底（右下角 max ≤40）", outside <= 40,
                        "band(200..290,10..390) max=" + outside);
                    p.Dispose();
                }
            }
            finally
            {
                GlobalEventBus.Unsubscribe(sub);
            }
        }

        // ==================================================================
        //  文字位置：九宫格锚点 / 边距 / 字号倍率（用户 2026-10-14 提"没办法控制文字位置"后新增）
        //
        //  【断言怎么搭】plan 级只比**关系**，不把 DisplayDraw 的公式再抄一遍：
        //  先用默认锚点跑一次，从 plan 里**量出**自动边距（Texts[0].Col）与行距（相邻行 Row 之差），
        //  再拿这两个实测值去核对九个锚点、自定义边距与缩放。公式改了断言跟着变，
        //  但"左中/右中/居中三种对齐的列含义不同""下带要留一个边距"这类**语义**跑不掉。
        //  像素级只补一条 plan 层证不了的：右下锚点的白框真的出现在右下、左上角于是干净。
        // ==================================================================
        private static void TextPosition()
        {
            Section("[RD-文字位置] 结果显示：九宫格锚点 / 边距 / 字号倍率（plan 关系 + 像素）");

            const int W = 400, H = 300;

            // ---- 基准：默认锚点 + 自动边距，量出参考值 ----
            var base0 = Stack(3, W, H, TextAnchorKind.TopLeft, 0, 1.0);
            double autoMargin = base0.Plan.Texts[0].Col;
            double gap = base0.Plan.Texts[1].Row - base0.Plan.Texts[0].Row;
            Check("基准：3 行堆叠、自动边距与行距可测（边距>0 且行距>字号）",
                base0.Plan.Texts.Count == 3 && autoMargin > 0 && gap > base0.Plan.Texts[0].FontSize,
                $"margin={autoMargin} gap={gap} font={base0.Plan.Texts[0].FontSize}");
            base0.DisposeTemps();

            // ---- U1：不传新参 == 显式默认（旧方案与旧 .vms 的行为不许变）----
            {
                var legacy = StackDefault3(W, H);
                var explicitDefault = Stack(3, W, H, TextAnchorKind.TopLeft, 0, 1.0);
                Check("U1 新增三参的默认值 == 引入前的行为（逐条比 Text/Row/Col/FontSize/Box/HAlign）",
                    SameTexts(legacy, explicitDefault),
                    "legacy=" + legacy.Plan.Texts.Count + " 条 explicit=" + explicitDefault.Plan.Texts.Count + " 条");
                legacy.DisposeTemps();
                explicitDefault.DisposeTemps();
            }

            // ---- U2 水平三列：col 含义与 HAlign ----
            {
                var l = Stack(3, W, H, TextAnchorKind.MiddleLeft, 0, 1.0);
                var c = Stack(3, W, H, TextAnchorKind.MiddleCenter, 0, 1.0);
                var r = Stack(3, W, H, TextAnchorKind.MiddleRight, 0, 1.0);
                Check("U2 左列 col == 自动边距且 HAlign=Left",
                    Eq(l.Plan.Texts[0].Col, autoMargin) && l.Plan.Texts.All(t => t.HAlign == BakeHAlign.Left),
                    $"col={l.Plan.Texts[0].Col} align={l.Plan.Texts[0].HAlign}");
                Check("U2 中列 col == 画布宽一半且 HAlign=Center",
                    Eq(c.Plan.Texts[0].Col, W / 2.0) && c.Plan.Texts.All(t => t.HAlign == BakeHAlign.Center),
                    $"col={c.Plan.Texts[0].Col} align={c.Plan.Texts[0].HAlign}");
                Check("U2 右列 col == 画布宽−边距且 HAlign=Right（右边缘贴画布，宽度由烘焙件实测）",
                    Eq(r.Plan.Texts[0].Col, W - autoMargin) && r.Plan.Texts.All(t => t.HAlign == BakeHAlign.Right),
                    $"col={r.Plan.Texts[0].Col} align={r.Plan.Texts[0].HAlign}");
                l.DisposeTemps(); c.DisposeTemps(); r.DisposeTemps();
            }

            // ---- U3 垂直三带：首行 row；中带上下留白相等 ----
            {
                var top = Stack(3, W, H, TextAnchorKind.TopCenter, 0, 1.0);
                var mid = Stack(3, W, H, TextAnchorKind.MiddleCenter, 0, 1.0);
                var bot = Stack(3, W, H, TextAnchorKind.BottomCenter, 0, 1.0);
                const int n = 3;
                Check("U3 上带首行 row == 边距",
                    Eq(top.Plan.Texts[0].Row, autoMargin), $"row={top.Plan.Texts[0].Row} margin={autoMargin}");
                Check("U3 中带整块垂直居中（上留白 == 下留白，±1px）",
                    Math.Abs(mid.Plan.Texts[0].Row - (H - n * gap - mid.Plan.Texts[0].Row)) <= 1.0,
                    $"上留白={mid.Plan.Texts[0].Row} 下留白={H - n * gap - mid.Plan.Texts[0].Row}");
                Check("U3 下带末行底边留一个边距（row[n-1] == 画布高−边距−行距）",
                    Eq(bot.Plan.Texts[n - 1].Row, H - autoMargin - gap, 0.5),
                    $"末行={bot.Plan.Texts[n - 1].Row} 期望≈{H - autoMargin - gap}");
                var overflow = Stack(40, W, H, TextAnchorKind.BottomRight, 0, 3.0);
                Check("U3 行数多到块比画布高时首行不推到负行号（夹回 ≥0，宁可下面溢出也不丢首行）",
                    overflow.Plan.Texts.All(t => t.Row >= 0),
                    "min row=" + overflow.Plan.Texts.Min(t => t.Row));
                top.DisposeTemps(); mid.DisposeTemps(); bot.DisposeTemps(); overflow.DisposeTemps();
            }

            // ---- U4 同带同首行、同列同 col（九宫格是"带 × 列"的积，不是九个独立公式）----
            {
                var a = Stack(3, W, H, TextAnchorKind.TopLeft, 0, 1.0);
                var b = Stack(3, W, H, TextAnchorKind.TopCenter, 0, 1.0);
                var cc = Stack(3, W, H, TextAnchorKind.TopRight, 0, 1.0);
                var d = Stack(3, W, H, TextAnchorKind.MiddleLeft, 0, 1.0);
                Check("U4 同一水平带的三个锚点首行 row 完全相同",
                    Eq(a.Plan.Texts[0].Row, b.Plan.Texts[0].Row) && Eq(a.Plan.Texts[0].Row, cc.Plan.Texts[0].Row),
                    $"{a.Plan.Texts[0].Row}/{b.Plan.Texts[0].Row}/{cc.Plan.Texts[0].Row}");
                Check("U4 换水平列不影响垂直（左上与左中首行不同、但两者 col 相同）",
                    !Eq(a.Plan.Texts[0].Row, d.Plan.Texts[0].Row) && Eq(a.Plan.Texts[0].Col, d.Plan.Texts[0].Col),
                    $"row {a.Plan.Texts[0].Row} vs {d.Plan.Texts[0].Row}，col {a.Plan.Texts[0].Col} vs {d.Plan.Texts[0].Col}");
                a.DisposeTemps(); b.DisposeTemps(); cc.DisposeTemps(); d.DisposeTemps();
            }

            // ---- U5 边距：显式值同时管住左右与上下；0 == 自动 ----
            {
                var m = Stack(3, W, H, TextAnchorKind.TopLeft, 40, 1.0);
                var mr = Stack(3, W, H, TextAnchorKind.TopRight, 40, 1.0);
                var mb = Stack(3, W, H, TextAnchorKind.BottomLeft, 40, 1.0);
                var zero = Stack(3, W, H, TextAnchorKind.TopLeft, 0, 1.0);
                var auto = Stack(3, W, H, TextAnchorKind.TopLeft, (int)autoMargin, 1.0);
                Check("U5 TextMargin=40 → 左列 col 与上带首行 row 都是 40",
                    Eq(m.Plan.Texts[0].Col, 40) && Eq(m.Plan.Texts[0].Row, 40),
                    $"col={m.Plan.Texts[0].Col} row={m.Plan.Texts[0].Row}");
                Check("U5 TextMargin=40 → 右列 col == 画布宽−40、下带末行底边留 40",
                    Eq(mr.Plan.Texts[0].Col, W - 40) && Eq(mb.Plan.Texts[2].Row, H - 40 - gap, 0.5),
                    $"右列 col={mr.Plan.Texts[0].Col}，下带末行={mb.Plan.Texts[2].Row}");
                Check("U5 TextMargin=0 == '用自动值'（与显式填自动边距出同一份 plan）",
                    SameTexts(zero, auto),
                    $"0→{zero.Plan.Texts[0].Col}，自动={autoMargin}→{auto.Plan.Texts[0].Col}");
                m.DisposeTemps(); mr.DisposeTemps(); mb.DisposeTemps(); zero.DisposeTemps(); auto.DisposeTemps();
            }

            // ---- U6 字号倍率：堆叠块与几何标签一起放大，行距跟着字号走 ----
            {
                var one = Stack(3, W, H, TextAnchorKind.TopLeft, 0, 1.0);
                var two = Stack(3, W, H, TextAnchorKind.TopLeft, 0, 2.0);
                int f1 = one.Plan.Texts[0].FontSize, f2 = two.Plan.Texts[0].FontSize;
                double gap2 = two.Plan.Texts[1].Row - two.Plan.Texts[0].Row;
                Check("U6 TextScale=2 → 字号翻倍、行距同步变大",
                    f2 == f1 * 2 && gap2 > gap,
                    $"字号 {f1}→{f2}，行距 {gap}→{gap2}");

                var geo1 = Build(GeometryParam.Circle(200, 160, 60), W, H);
                var geo2Items = new List<DisplayItem>
                {
                    new(new DisplayItemDef { Id = "r1", Label = "圆", Enabled = true }, GeometryParam.Circle(200, 160, 60))
                };
                var geo2 = DisplayDraw.Build(geo2Items, W, H, true, "", "NG", 300,
                    TextAnchorKind.TopLeft, 0, 2.0);
                Check("U6 几何标签（跟着图形走的那种）字号同步放大",
                    geo2.Plan.Texts.Count > 0 && geo1.Plan.Texts.Count > 0 &&
                    geo2.Plan.Texts[0].FontSize == geo1.Plan.Texts[0].FontSize * 2,
                    $"标签字号 {(geo1.Plan.Texts.Count > 0 ? geo1.Plan.Texts[0].FontSize : -1)}→" +
                    $"{(geo2.Plan.Texts.Count > 0 ? geo2.Plan.Texts[0].FontSize : -1)}");
                Check("U6 放大只改字号，不改几何（半径/圆心参数一字不动）",
                    geo1.Plan.Shapes.Count == geo2.Plan.Shapes.Count &&
                    geo1.Plan.Shapes.Zip(geo2.Plan.Shapes, (x, y) => x.P.SequenceEqual(y.P)).All(z => z),
                    $"shapes {geo1.Plan.Shapes.Count} vs {geo2.Plan.Shapes.Count}");

                // 越界夹取：与夹到边界值的结果**完全一致**（不是"看起来差不多"）
                var lo = Stack(2, W, H, TextAnchorKind.TopLeft, 0, 0.1);
                var loClamped = Stack(2, W, H, TextAnchorKind.TopLeft, 0, 0.5);
                var hi = Stack(2, W, H, TextAnchorKind.TopLeft, 0, 9.0);
                var hiClamped = Stack(2, W, H, TextAnchorKind.TopLeft, 0, 3.0);
                Check("U6 倍率越界夹到 0.5~3.0（0.1 与 0.5 出同一份 plan、9.0 与 3.0 同理）",
                    SameTexts(lo, loClamped) && SameTexts(hi, hiClamped),
                    $"0.1→{lo.Plan.Texts[0].FontSize}，0.5→{loClamped.Plan.Texts[0].FontSize}，" +
                    $"9.0→{hi.Plan.Texts[0].FontSize}，3.0→{hiClamped.Plan.Texts[0].FontSize}");

                one.DisposeTemps(); two.DisposeTemps(); geo1.DisposeTemps(); geo2.DisposeTemps();
                lo.DisposeTemps(); loClamped.DisposeTemps(); hi.DisposeTemps(); hiClamped.DisposeTemps();
            }

            // ---- U7 关掉叠字：任何锚点都不该有文字（锚点不能变成"强行出字"）----
            {
                var off = Stack(3, W, H, TextAnchorKind.BottomRight, 0, 1.0, showText: false);
                Check("U7 关掉图上叠字时右下锚点也零文字（锚点只管位置，不管出不出字）",
                    off.Plan.Texts.Count == 0, "texts=" + off.Plan.Texts.Count);
                off.DisposeTemps();
            }

            // ---- U8 像素级：右下锚点的白框真在右下、左上角变干净 ----
            {
                const int W2 = 400, H2 = 300;
                using var img = BlackCanvas(W2, H2);
                var p = NewPlugin(0, img, It("r1", 42.0, "F2", "mm"), It("r2", 7), It("r3", 99));
                p.TextAnchor = TextAnchorKind.BottomRight;
                var (ctx, log) = NewContext();
                p.Execute(ctx);
                var product = p.AnnotatedImage.TypedValue;
                Check("U8 前置：右下锚点这一轮仍产出非 null（配置改动不该让烧录失败）",
                    product != null, product == null ? "(null) warns=[" + string.Join(" | ", log.Warns) + "]" : SizeOf(product).ToString());
                if (product != null)
                {
                    double br = BandMax(product, H2 - 60, W2 - 200, H2 - 1, W2 - 1);
                    double tl = BandMax(product, 0, 0, 60, 240);
                    Check("U8【像素】右下锚点 → 右下带采到白底框（max ≥240）",
                        br >= 240, "band(240..299,200..399) max=" + br + " ｜ warns=[" + string.Join(" | ", log.Warns) + "]");
                    Check("U8【像素】右下锚点 → 左上角让出来了（max ≤40：现场要的『别压住工件』就是这一条）",
                        tl <= 40, "band(0..60,0..240) max=" + tl);
                }
                p.Dispose();
            }

            // ---- U9 配置钩子：越界值在**入口**就被夹住（.vms 手改/老方案回填都走这里）----
            {
                using var img = BlackCanvas(W, H);
                var p = NewPlugin(1, img, It("r1", 1));
                p.TextAnchor = (TextAnchorKind)99;
                p.TextMargin = 99999;
                p.TextScale = double.NaN;
                Check("U9 非法锚点夹回左上、边距夹到 4096、NaN 倍率夹回 1.0（钩子在入口挡弹）",
                    p.TextAnchor == TextAnchorKind.TopLeft && p.TextMargin == 4096 && Eq(p.TextScale, 1.0),
                    $"anchor={p.TextAnchor} margin={p.TextMargin} scale={p.TextScale}");
                p.Dispose();
            }
        }

        /// <summary>造 n 行数值堆叠（预算给足 300，所以不会出现截断说明行干扰行数）</summary>
        private static DisplayDrawResult Stack(
            int n, int w, int h, TextAnchorKind anchor, int margin, double scale, bool showText = true)
        {
            var items = new List<DisplayItem>();
            for (int i = 0; i < n; i++)
                items.Add(new DisplayItem(
                    new DisplayItemDef
                    {
                        Id = "r" + i.ToString(CultureInfo.InvariantCulture),
                        Label = "项" + i.ToString(CultureInfo.InvariantCulture),
                        Enabled = true
                    },
                    (double)(i + 1)));
            return DisplayDraw.Build(items, w, h, showText, "", "NG", 300, anchor, margin, scale);
        }

        /// <summary>同样三行，但**不传**新增的三个参数 = 引入本功能之前的调用形态</summary>
        private static DisplayDrawResult StackDefault3(int w, int h)
        {
            var items = new List<DisplayItem>();
            for (int i = 0; i < 3; i++)
                items.Add(new DisplayItem(
                    new DisplayItemDef
                    {
                        Id = "r" + i.ToString(CultureInfo.InvariantCulture),
                        Label = "项" + i.ToString(CultureInfo.InvariantCulture),
                        Enabled = true
                    },
                    (double)(i + 1)));
            return DisplayDraw.Build(items, w, h, true, "", "NG", 300);
        }

        /// <summary>两份 plan 的文字层逐条同构（含 HAlign）——"默认值 == 老行为"只能用这种硬比对</summary>
        private static bool SameTexts(DisplayDrawResult a, DisplayDrawResult b) =>
            a.Plan.Texts.Count == b.Plan.Texts.Count &&
            a.Plan.Texts.Zip(b.Plan.Texts, (x, y) =>
                x.Text == y.Text && Eq(x.Row, y.Row) && Eq(x.Col, y.Col) &&
                x.FontSize == y.FontSize && x.Box == y.Box && x.HAlign == y.HAlign).All(ok => ok);

        // ==================================================================
        //  A6 / A7 / A9：配置生命周期（OnConfirm → ApplyConfigValues 往返、Id 稳定、试运行污染）
        // ==================================================================
        private static void ConfigLifecycle()
        {
            Section("[RD-A6/A7/A9] 结果显示：配置生命周期（往返不丢手填常量 / Id 稳定 / 试运行污染）");

            // ---- A6：OnConfirm → 新实例 ApplyConfigValues 往返 ----
            {
                var p1 = new ResultDisplayPlugin { InstanceName = "结果显示.往返" };
                p1.Items.Add(new DisplayItemDef { Id = "r2", Label = "手填项", Format = "F2", Unit = "mm" });
                p1.GetInputPort("r2")!.Value = 12.5;           // 用户在手填框里敲的常量
                p1.DisplayViewIndex = 4;
                p1.ShowTextOnImage = false;
                p1.JudgeItemLabel = "手填项";
                p1.NgValue = "NG";
                p1.MaxDrawElements = 77;
                p1.TextAnchor = TextAnchorKind.BottomRight;
                p1.TextMargin = 30;
                p1.TextScale = 1.5;

                var fake = new LinkableFakeStepData();
                p1.OnConfirm(fake);
                string keys = string.Join(",", fake.InputValues.Keys);
                Check("A6 OnConfirm 把结果项表写进了 InputValues（[StepConfig] 快照）",
                    fake.InputValues.ContainsKey("Items"), "keys=[" + keys + "]");

                var p2 = new ResultDisplayPlugin();
                p2.ApplyConfigValues(fake);

                Check("A6 往返后动态端口存在（r1 出厂项 + r2 新增项）",
                    p2.Inputs.ContainsKey("r1") && p2.Inputs.ContainsKey("r2"),
                    "inputs=[" + string.Join(",", p2.Inputs.Keys) + "]");
                object r2Value = p2.GetInputPort("r2")?.Value;
                Check("A6【§6.4-2 那个坑】往返后**手填常量不丢**（基类遍历 InputValues 时动态口还没建 → 必须补灌一遍）",
                    r2Value is double d && Math.Abs(d - 12.5) < 1e-9,
                    "r2='" + (r2Value ?? "(null)") + "'");
                Check("A6 往返后端口显示名 = Label（不是 Id）",
                    p2.GetInputPort("r2")?.Description == "手填项",
                    "'" + p2.GetInputPort("r2")?.Description + "'");
                Check("A6 往返后其余 [StepConfig] 全部还原（窗口号/叠字/判定项/NG值/上限/**文字位置三项**）",
                    p2.DisplayViewIndex == 4 && p2.ShowTextOnImage == false
                    && p2.JudgeItemLabel == "手填项" && p2.NgValue == "NG" && p2.MaxDrawElements == 77
                    && p2.TextAnchor == TextAnchorKind.BottomRight && p2.TextMargin == 30
                    && Math.Abs(p2.TextScale - 1.5) < 1e-9,
                    "view=" + p2.DisplayViewIndex + " text=" + p2.ShowTextOnImage
                    + " judge='" + p2.JudgeItemLabel + "' ng='" + p2.NgValue + "' max=" + p2.MaxDrawElements
                    + " anchor=" + p2.TextAnchor + " margin=" + p2.TextMargin + " scale=" + p2.TextScale);
                Check("A6 往返后结果项表的 Format/Unit 也在（JSON 往返，不是只存了 Id）",
                    p2.Items.Count == 2 && p2.Items[1].Format == "F2" && p2.Items[1].Unit == "mm",
                    "items=" + p2.Items.Count
                    + " fmt='" + (p2.Items.Count > 1 ? p2.Items[1].Format : "") + "'"
                    + " unit='" + (p2.Items.Count > 1 ? p2.Items[1].Unit : "") + "'");

                p1.Dispose();
                p2.Dispose();
            }

            // ---- A7：改 Label 不断线（Id 才是端口名）；删行端口消失 ----
            {
                var p = new ResultDisplayPlugin { InstanceName = "结果显示.改名" };
                var def = p.Items[0];
                string idBefore = def.Id;
                int versionBefore = p.PortsVersion;

                def.Label = "改了个名字";
                Check("A7 改 Label 后 **Id（=端口名）不变**（FlowCompiler.cs:1073：端口名一改就是断线）",
                    def.Id == idBefore && p.Inputs.ContainsKey(idBefore),
                    "Id='" + def.Id + "'（原 '" + idBefore + "'） inputs=[" + string.Join(",", p.Inputs.Keys) + "]");
                Check("A7 改 Label 后端口显示名跟着变（Label 只影响显示，不影响连线）",
                    p.Inputs[idBefore].Description == "改了个名字", "'" + p.Inputs[idBefore].Description + "'");
                Check("A7 改 Label 触发 PortsVersion 递增（视图行内 Port MultiBinding 的重取锚点）",
                    p.PortsVersion > versionBefore, versionBefore + " → " + p.PortsVersion);

                def.Enabled = false;
                Check("A7 停用一项 → 对应端口消失（不建口 = 不参与烧录，配置原样留着）",
                    !p.Inputs.ContainsKey(idBefore), "inputs=[" + string.Join(",", p.Inputs.Keys) + "]");
                def.Enabled = true;
                Check("A7 重新启用 → 端口回来，Id 仍是原来那个",
                    p.Inputs.ContainsKey(idBefore), "inputs=[" + string.Join(",", p.Inputs.Keys) + "]");

                p.Items.Remove(def);
                Check("A7 删行后对应端口消失", !p.Inputs.ContainsKey(idBefore),
                    "inputs=[" + string.Join(",", p.Inputs.Keys) + "]");

                var a = p.AddItem();
                var b = p.AddItem();
                Check("A7 AddItem 生成的 Id 互不重名（重名 Id 只会建出一个口 → 静默丢一项）",
                    a.Id != b.Id && p.Inputs.ContainsKey(a.Id) && p.Inputs.ContainsKey(b.Id),
                    a.Id + " / " + b.Id);
                p.Dispose();
            }

            // ---- A7b：判定项候选（R35：可编辑 ComboBox 换 ItemsSource 会清空 Text 并推回空串）----
            {
                var p = new ResultDisplayPlugin { InstanceName = "结果显示.候选" };
                p.Items.Add(new DisplayItemDef { Id = "r2", Label = "判定" });
                string labels = string.Join("|", p.JudgeItemLabels);
                Check("A7b 候选 = 首项空串（不判定的显式出口）+ 启用项的 **Label**（不含 Id）",
                    p.JudgeItemLabels.Count == 3 && p.JudgeItemLabels[0] == ""
                    && p.JudgeItemLabels.Contains("结果1") && p.JudgeItemLabels.Contains("判定")
                    && !p.JudgeItemLabels.Contains("r1") && !p.JudgeItemLabels.Contains("r2"),
                    "[" + labels + "]");

                p.JudgeItemLabel = "判定";
                p.Items[1].Label = "判定2";     // 触发候选重算
                Check("A7b【R35】重算候选后当前判定项**没有被清空**（ItemsSource 整体替换会把空串推回绑定源）",
                    p.JudgeItemLabel == "判定", "'" + p.JudgeItemLabel + "'");

                p.JudgeItemLabel = "手填的一个不在表里的名字";
                Check("A7b 手填的名字留在候选末项（否则 ComboBox 显示不出选中态，用户以为配置丢了）",
                    p.JudgeItemLabels[p.JudgeItemLabels.Count - 1] == "手填的一个不在表里的名字",
                    "[" + string.Join("|", p.JudgeItemLabels) + "]");
                p.Dispose();
            }

            // ---- A9：试运行污染回归（PluginTestRunner.BridgeInputs 把上游 HImage 灌进配置实例的端口）----
            {
                var p = new ResultDisplayPlugin { InstanceName = "结果显示.污染" };
                p.Items.Add(new DisplayItemDef { Id = "r2", Label = "区域" });
                using var img = BlackCanvas(64, 48);
                HOperatorSet.GenRectangle1(out HObject hr, 5, 5, 20, 20);
                using var region = new HRegion(hr);

                // 模拟 BridgeInputs：configPort.Value = actual（上游实际值灌进配置实例）
                p.SrcImage.Value = img;
                p.GetInputPort("r1")!.Value = img;
                p.GetInputPort("r2")!.Value = region;

                var fake = new LinkableFakeStepData();
                p.OnConfirm(fake);
                string dump = DumpValues(fake);

                bool polluted = fake.InputValues.Values.Any(v => v is string s && s.Contains("Halcon"));
                Check("A9【试运行污染】InputValues 里没有 \"HalconDotNet.HImage\" 这类类型名字面量",
                    !polluted, dump);
                Check("A9 HImage 口（SrcImage / r1）与 HRegion 口（r2）都没被当常量落盘",
                    !fake.InputValues.ContainsKey("SrcImage") && !fake.InputValues.ContainsKey("r1")
                    && !fake.InputValues.ContainsKey("r2"), dump);
                Check("A9 守卫**没有误伤** [StepConfig] 配置项（Items 集合仍在）",
                    fake.InputValues.ContainsKey("Items"), dump);
                p.Dispose();
            }
            {
                // 真常量必须留下：这是守卫"只挡非托管对象、不一刀切"的正面证据
                var p = new ResultDisplayPlugin { InstanceName = "结果显示.常量" };
                p.Items.Add(new DisplayItemDef { Id = "r2", Label = "手填" });
                p.GetInputPort("r1")!.Value = "OK";
                p.GetInputPort("r2")!.Value = 21;
                var fake = new LinkableFakeStepData();
                p.OnConfirm(fake);
                string dump = DumpValues(fake);
                Check("A9 手填 string / int 常量在 OnConfirm 后**留在** InputValues（守卫不许一刀切）",
                    fake.InputValues.TryGetValue("r1", out var v1) && Equals(v1, "OK")
                    && fake.InputValues.TryGetValue("r2", out var v2) && Equals(v2, 21), dump);
                Check("A9 [StepConfig] 数值项也在（DisplayViewIndex / MaxDrawElements）",
                    fake.InputValues.ContainsKey("DisplayViewIndex") && fake.InputValues.ContainsKey("MaxDrawElements"), dump);
                p.Dispose();
            }
        }

        // ==================================================================
        //  A8：宁缺显示不停线 —— 认不出的类型只丢自己，绝不 Fail、绝不抛
        // ==================================================================
        private static void NeverStopLine()
        {
            Section("[RD-A8] 结果显示：宁缺显示不停线（认不出的类型只丢自己，Success 恒 true）");

            const int W = 320, H = 240;

            // ---- ① object 口分别喂 HImage / HTuple / 自定义模型 / HRegion / null ----
            {
                using var img = BlackCanvas(W, H);
                using var extra = BlackCanvas(32, 24);
                HOperatorSet.GenRectangle1(out HObject hr, 20, 20, 80, 120);
                using var region = new HRegion(hr);
                var tuple = new HTuple(new double[] { 1.5, 2.5, 3.5 });
                try
                {
                    var p = new ResultDisplayPlugin { InstanceName = "结果显示.A8", DisplayViewIndex = 0 };
                    p.Items.Clear();
                    p.Items.Add(new DisplayItemDef { Id = "r1", Label = "图", Enabled = true });
                    p.Items.Add(new DisplayItemDef { Id = "r2", Label = "元组", Enabled = true });
                    p.Items.Add(new DisplayItemDef { Id = "r3", Label = "怪模型", Enabled = true });
                    p.Items.Add(new DisplayItemDef { Id = "r4", Label = "区域", Enabled = true });
                    p.Items.Add(new DisplayItemDef { Id = "r5", Label = "空的", Enabled = true });
                    p.RebuildDynamicInputs();
                    p.SrcImage.Value = img;
                    p.GetInputPort("r1")!.Value = extra;              // HImage → 拒收（底图只有 SrcImage 一个入口）
                    p.GetInputPort("r2")!.Value = tuple;              // HTuple → 逐元素格式化
                    p.GetInputPort("r3")!.Value = new WeirdModel();   // 非 IConvertible → 红字写清类型全名
                    p.GetInputPort("r4")!.Value = region;             // HRegion → 照画
                    // r5 不赋值 → null → 不画，但悬停里要能看出"这项是空的"

                    var (ctx, log) = NewContext();
                    string threw = "";
                    bool ret = false;
                    try { ret = p.Execute(ctx); }
                    catch (Exception ex) { threw = ex.GetType().Name + ": " + ex.Message; }

                    Check("A8 喂 HImage / HTuple / 自定义模型 / HRegion / null → **不抛异常**",
                        threw.Length == 0, threw);
                    Check("A8 Success==true 且 Execute 返回 true（显示算子失败会拖垮整轮，FlowInvoker.cs:132-133）",
                        ret && p.Success.Value is true && string.IsNullOrEmpty(p.ErrorMessage.TypedValue),
                        "ret=" + ret + " Success=" + p.Success.Value + " Err='" + p.ErrorMessage.TypedValue + "'");
                    Check("A8 DisplayedCount == 4（图/元组/怪模型/区域各画了一个元素；空值项不计）",
                        p.DisplayedCount.TypedValue == 4, p.DisplayedCount.TypedValue.ToString(CultureInfo.InvariantCulture));
                    Check("A8 拒收图像值有 Warn 且指名是哪一项",
                        log.HasWarn("不支持图像值") && log.HasWarn("「图」"), string.Join(" | ", log.Warns));
                    Check("A8 认不出的类型有 Warn 且写清**类型全名**（R34 宽进严出：被拒的原值要留痕）",
                        log.HasWarn("不支持的类型") && log.HasWarn("WeirdModel"), string.Join(" | ", log.Warns));
                    Check("A8 没有 Error 级日志（降级不是故障）",
                        log.Errors.Count == 0, string.Join(" | ", log.Errors));
                    p.Dispose();
                }
                finally { tuple.Dispose(); }
            }

            // ---- ② 没有底图：只 Warn，不 Fail，什么都不发 ----
            {
                var p = new ResultDisplayPlugin { InstanceName = "结果显示.无底图", DisplayViewIndex = 1 };
                var (ctx, log) = NewContext();
                bool ret = p.Execute(ctx);
                Check("A8 底图为空 → Success 仍为 true、Execute 返回 true（宁缺显示不停线）",
                    ret && p.Success.Value is true, "ret=" + ret + " Success=" + p.Success.Value);
                Check("A8 底图为空 → 有 Warn 指名是 SrcImage，且 IsDisplayed=false、产物为 null",
                    log.HasWarn("底图（SrcImage）为空") && p.IsDisplayed.Value is false
                    && (p.AnnotatedImage.TypedValue == null || !p.AnnotatedImage.TypedValue.IsInitialized()),
                    "warns=[" + string.Join(" | ", log.Warns) + "] IsDisplayed=" + p.IsDisplayed.Value);
                p.Dispose();
            }

            // ---- ③ 垃圾格式串：格式化失败退回默认，绝不抛 ----
            {
                using var img = BlackCanvas(W, H);
                var p = NewPlugin(0, img, It("r1", 12.3456, "{"));
                var (ctx, log) = NewContext();
                string threw = "";
                try { p.Execute(ctx); } catch (Exception ex) { threw = ex.Message; }
                Check("A8 垃圾格式串 \"{\" → 不抛、退回默认格式、步骤仍成功",
                    threw.Length == 0 && p.Success.Value is true && p.DisplayedCount.TypedValue == 1,
                    threw.Length > 0 ? threw : "Success=" + p.Success.Value + " count=" + p.DisplayedCount.TypedValue
                        + " warns=[" + string.Join(" | ", log.Warns) + "]");
                p.Dispose();
            }

            // ---- ④ 判定口径：bool 与 "NG" 必须能对上（本文件自己的显示口径就是 OK/NG）----
            {
                string ColorsOf(DisplayDrawResult r) =>
                    "[" + string.Join(",", r.Plan.Texts.Select(t => t.Color).Distinct()) + "]";

                var ng = BuildMixed(W, H, "判定", "NG", false);
                Check("A8 判定项是 bool false、NG 值填 \"NG\" → 判成 NG（否则出现'字写着 NG、颜色却是绿的'）",
                    ng.Plan.Texts.Any(t => t.Color == "red"), ColorsOf(ng));
                ng.DisposeTemps();

                var ok = BuildMixed(W, H, "判定", "NG", true);
                Check("A8 判定项是 bool true、NG 值填 \"NG\" → 判成 OK（不含 red）",
                    ok.Plan.Texts.All(t => t.Color != "red"), ColorsOf(ok));
                ok.DisposeTemps();

                var num = BuildMixed(W, H, "判定", "21", 21.0);
                Check("A8 数值智能比对：21.0 能对上 NG 值 \"21\"",
                    num.Plan.Texts.Any(t => t.Color == "red"), ColorsOf(num));
                num.DisposeTemps();

                // R34：数字解析必须拒绝千分位（NumberStyles.Any 会把 "1,5" 读成 15）
                var thou = BuildMixed(W, H, "判定", "1,5", 15.0);
                Check("A8【R34】NG 值 \"1,5\" **不许**被读成 15（千分位一律拒绝）",
                    thou.Plan.Texts.All(t => t.Color != "red"), ColorsOf(thou));
                thou.DisposeTemps();

                var byId = BuildMixed(W, H, "r3", "NG", "NG");
                Check("A8 判定项按 Label 找不到时**退回按 Id 找**（宽容，配错也不至于静默不判定）",
                    byId.Plan.Texts.Any(t => t.Color == "red"), ColorsOf(byId));
                byId.DisposeTemps();
            }
        }

        // ==================================================================
        //  C3 / C4：缩略图与导出 PNG 都自动带标注（烧录相对覆盖层的核心收益）
        // ==================================================================
        private static void ThumbnailAndExport()
        {
            Section("[RD-C3/C4] 结果显示：缩略图与导出 PNG 都带标注（宿主零改动）");

            const int W = 320, H = 240;
            var settings = new AppSettingsService();
            var cfg = settings.Current.ImageGallery;
            int savedMax = cfg.ThumbnailMaxSize;
            bool savedEnabled = cfg.Enabled, savedRealtime = cfg.IncludeRealtimePreviews;
            string exportDir = Path.Combine(Path.GetTempPath(), "rd-export-" + Guid.NewGuid().ToString("N"));
            try
            {
                cfg.ThumbnailMaxSize = 160;
                cfg.Enabled = true;
                cfg.IncludeRealtimePreviews = true;     // ★ 不开就是静默零帧，断言会假绿（Capture 有前置门）

                var svc = new ImageCollectionService(settings, null!);
                Check("C3 前置：图集把自己注册为预览捕获钩子（发布线程同步拷贝）",
                    ReferenceEquals(ImagePreviewCaptureHub.Current, svc),
                    ImagePreviewCaptureHub.Current?.GetType().Name ?? "(null)");
                try
                {
                    // 1 号窗口发一张**纯黑对照**，2 号窗口发烧录产物 —— 两张都在图集里，逐帧对比
                    using var black = BlackCanvas(W, H);
                    new PubStub { InstanceName = "结果显示断言.对照" }.PublishPreview(black, 1);

                    using var baseImg = BlackCanvas(W, H);
                    var p = NewPlugin(2, baseImg,
                        It("r1", GeometryParam.Circle(120, 160, 70)),
                        It("r2", 42.5, "F2", "mm"));
                    var (ctx, log) = NewContext();
                    p.Execute(ctx);

                    Check("C3 图集收到两帧（对照 + 产物）", svc.Count == 2,
                        "帧数=" + svc.Count + " warns=[" + string.Join(" | ", log.Warns) + "]");

                    var plain = svc.Frames.FirstOrDefault(f => f.ViewIndex == 1);
                    var shot = svc.Frames.FirstOrDefault(f => f.ViewIndex == 2);
                    Check("C3 产物那一帧的缩略图已生成",
                        shot?.Thumbnail != null,
                        shot?.Thumbnail == null ? "(Thumbnail 为 null)"
                            : shot.Thumbnail.PixelWidth + "x" + shot.Thumbnail.PixelHeight);

                    if (plain?.Thumbnail != null && shot?.Thumbnail != null)
                    {
                        byte plainMax = MaxOf(plain.Thumbnail), shotMax = MaxOf(shot.Thumbnail);
                        Check("C3【烧录的核心收益】**缩略图**上采得到标注（产物 max ≥150，纯黑对照 max ≤20）",
                            shotMax >= 150 && plainMax <= 20, "产物=" + shotMax + " 对照=" + plainMax);
                    }

                    // ---- C4：导出 PNG 落盘 → 读回 → 还能采到标注色 ----
                    int saved = shot == null ? 0 : ImageExporter.SaveFrames(new[] { shot }, exportDir);
                    Check("C4 ImageExporter.SaveFrames 落盘 1 个文件", saved == 1,
                        "saved=" + saved.ToString(CultureInfo.InvariantCulture));

                    var pngs = Directory.Exists(exportDir)
                        ? Directory.GetFiles(exportDir, "*.png", SearchOption.AllDirectories)
                        : Array.Empty<string>();
                    Check("C4 目录里确实有 PNG（文件名 = 步骤_端口_时分秒毫秒）",
                        pngs.Length == 1,
                        pngs.Length == 0 ? "(没有文件)" : string.Join(",", pngs.Select(Path.GetFileName)));

                    if (pngs.Length == 1)
                    {
                        HOperatorSet.ReadImage(out HObject raw, pngs[0]);
                        using var back = new HImage(raw);
                        var product = p.AnnotatedImage.TypedValue;
                        Check("C4 读回的 PNG 尺寸 == 产物尺寸（导出没有二次缩放）",
                            SizeOf(back) == SizeOf(product),
                            "读回=" + SizeOf(back) + " 产物=" + SizeOf(product));
                        // 圆心 (120,160) 半径 70 ⇒ 圆顶 (50,160)
                        double g = ChannelMax(back, 2, 45, 155, 55, 165);
                        double r = ChannelMax(back, 1, 45, 155, 55, 165);
                        Check("C4【导出带标注】读回的 PNG 上仍采得到 cyan（绿 ≥200 且红 ≤60）",
                            g >= 200 && r <= 60, "绿=" + g + " 红=" + r);
                        double box = BandMax(back, 8, 8, 60, 260);
                        Check("C4 读回的 PNG 上仍采得到文字白底框（max ≥240）", box >= 240, "max=" + box);
                    }

                    p.Dispose();
                }
                finally
                {
                    svc.Clear();
                    ImagePreviewCaptureHub.Current = null;    // 不把钩子留给后续用例
                }
            }
            finally
            {
                cfg.ThumbnailMaxSize = savedMax;
                cfg.Enabled = savedEnabled;
                cfg.IncludeRealtimePreviews = savedRealtime;
                try { if (Directory.Exists(exportDir)) Directory.Delete(exportDir, true); }
                catch { /* 临时目录清理失败不影响断言结论 */ }
            }
        }

        // ==================================================================
        //  静态扫描（6 条防回归，成本低、跑得快）
        // ==================================================================

        /// <summary>
        /// 共享件 <c>AnnotationBaker.AllowedColors</c> 的镜像（那份是 private，扫源码时只能自己列一遍）。
        /// ★ 改了共享件的白名单，这里必须同步 —— A5 那条断言会因此变红，是故意的。
        /// </summary>
        private static readonly HashSet<string> AllowedColors = new(StringComparer.OrdinalIgnoreCase)
        {
            "green", "red", "orange", "yellow", "cyan", "magenta", "white"
        };

        private static void StaticScans()
        {
            Section("[RD-静态扫描] 结果显示：16 条源码级防回归（①②③-a/b/c④④b④c⑤⑥⑥b⑦⑦b⑧⑧b⑧c）");

            var root = RepoRoot();
            Check("静态扫描前置：能定位仓库根（以 AGENTS.md 为锚）", root != null, root ?? "(未找到)");
            if (root == null) return;

            // 只扫第一方目录：DLL\HslCommunication、VM3.0-main 是随仓的第三方源码（R29 同族噪音）
            var files = new[] { "Plugins", "Services", "Shard", "Engine", "Core", "VisionMaster" }
                .Select(d => Path.Combine(root, d))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
                .Where(f => !IsBuildDir(f))
                .ToList();
            Check("静态扫描前置：扫到第一方 .cs 源文件", files.Count > 200, files.Count + " 个文件");

            string rdDir = Path.Combine(root, "Plugins", "Plugin.ResultDisplay");
            var rdFiles = Directory.Exists(rdDir)
                ? Directory.GetFiles(rdDir, "*.cs", SearchOption.AllDirectories).Where(f => !IsBuildDir(f)).ToList()
                : new List<string>();
            Check("静态扫描前置：扫到 Plugin.ResultDisplay 的源文件", rdFiles.Count >= 4, rdFiles.Count + " 个文件");
            string bakerFile = Path.Combine(root, "Services", "Core.Halcon", "Helpers", "AnnotationBaker.cs");

            // ---- ① 禁透传口：SrcImage 的值不许被赋给任何端口 ----
            {
                var hits = new List<string>();
                foreach (var f in rdFiles)
                {
                    string code = StripComments(Read(f));
                    foreach (Match m in Regex.Matches(code, @"\.\s*Value\s*=\s*[^\r\n;]*\bSrcImage\b"))
                        hits.Add(Rel(root, f) + ":" + LineOf(code, m.Index) + " " + OneLine(m.Value));
                }
                Check("扫描①【禁透传口】Plugin.ResultDisplay 里没有把 SrcImage 的值赋给任何端口",
                    hits.Count == 0, hits.Count == 0 ? "(无)" : string.Join(" | ", hits));
            }

            // ---- ② 空 catch：本插件与共享件里一个都不许有 ----
            {
                // 全仓有 64 处 `try { x.Dispose(); } catch { }`，那是**正确**的释放兜底，不该判红；
                // 所以这条只扫"本轮新增的两个文件族"，范围明确、判据严格。
                var hits = new List<string>();
                foreach (var f in rdFiles.Concat(new[] { bakerFile }))
                {
                    if (!File.Exists(f)) continue;
                    string raw = Read(f);
                    foreach (Match m in Regex.Matches(raw, @"catch\s*(\([^)]*\))?\s*\{\s*\}"))
                        hits.Add(Rel(root, f) + ":" + LineOf(raw, m.Index));
                }
                Check("扫描② Plugin.ResultDisplay 与 AnnotationBaker 里**零**空 catch（该吼的必须吼）",
                    hits.Count == 0, hits.Count == 0 ? "(无)" : string.Join(" | ", hits));
            }

            // ---- ③ 色名只走白名单 ----
            {
                // ★ 色名/画法必须**按实参位置**取（AddShape 第 3 个、AddObj 第 2 个、AddTextAt 第 4 个…）。
                //   早先的写法抓"调用里第一个字符串字面量"，把文案（中文）和 "margin" 当成了色名，
                //   一轮误报 10 条 —— 判据得落在语法位置上，不能落在"看起来像"上。
                var colorArg = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["AddShape"] = 2, ["BakeShape"] = 2,
                    ["AddObj"] = 1, ["BakeObj"] = 1,
                    ["AddTextLine"] = 1,
                    ["AddTextAt"] = 3, ["BakeText"] = 3,
                };
                var drawArg = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["AddObj"] = 3, ["BakeObj"] = 3,
                };
                var allowedDraw = new HashSet<string>(StringComparer.Ordinal) { "margin", "fill" };

                var bad = new List<string>();
                var seen = new SortedSet<string>(StringComparer.Ordinal);

                // (a) 色名常量表：const / static readonly string <名字含 Color> = "字面量"
                var colorConsts = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var f in rdFiles)
                {
                    string code = StripComments(Read(f));
                    foreach (Match m in Regex.Matches(code,
                        @"(?:const|static\s+readonly)\s+string\s+(?<n>\w*Color\w*)\s*=\s*""(?<v>[^""]*)"""))
                    {
                        colorConsts[m.Groups["n"].Value] = m.Groups["v"].Value;
                        seen.Add(m.Groups["v"].Value);
                        if (!AllowedColors.Contains(m.Groups["v"].Value))
                            bad.Add(Rel(root, f) + ":" + LineOf(code, m.Index) + " 常量 " + m.Groups["n"].Value
                                + "=\"" + m.Groups["v"].Value + "\"");
                    }
                }
                Check("扫描③-a 色名常量的字面值全在白名单内", bad.Count == 0 && colorConsts.Count >= 4,
                    bad.Count > 0 ? string.Join(" | ", bad)
                        : "常量=[" + string.Join(",", colorConsts.Select(kv => kv.Key + "=" + kv.Value)) + "]");

                // (b) 调用点：色名实参 / 画法实参
                bad.Clear();
                string callNames = string.Join("|", colorArg.Keys.Concat(drawArg.Keys).Distinct());
                foreach (var f in rdFiles)
                {
                    string code = StripComments(Read(f));
                    foreach (Match m in Regex.Matches(code, @"\b(?<n>" + callNames + @")\s*\("))
                    {
                        string name = m.Groups["n"].Value;
                        var args = SplitTopLevel(ArgsOf(code, m.Index + m.Length - 1));

                        if (colorArg.TryGetValue(name, out int ci) && ci < args.Count)
                        {
                            string arg = args[ci].Trim();
                            bool literal = arg.IndexOf('"') >= 0;
                            foreach (Match lit in Regex.Matches(arg, @"""([^""]*)"""))
                            {
                                seen.Add(lit.Groups[1].Value);
                                if (!AllowedColors.Contains(lit.Groups[1].Value))
                                    bad.Add(Rel(root, f) + ":" + LineOf(code, m.Index) + " " + name
                                        + " 色名=\"" + lit.Groups[1].Value + "\"");
                            }
                            // 不是字面量时，至少得看得出"这是个色名来源"（名字里带 Color）；
                            // 否则等于把 SetColor 的入参藏到扫描看不见的地方去了
                            if (!literal && arg.IndexOf("Color", StringComparison.OrdinalIgnoreCase) < 0)
                                bad.Add(Rel(root, f) + ":" + LineOf(code, m.Index) + " " + name
                                    + " 色名来源无法识别: " + OneLine(arg));
                        }

                        if (drawArg.TryGetValue(name, out int di) && di < args.Count)
                        {
                            string arg = args[di].Trim();
                            foreach (Match lit in Regex.Matches(arg, @"""([^""]*)"""))
                            {
                                if (!allowedDraw.Contains(lit.Groups[1].Value))
                                    bad.Add(Rel(root, f) + ":" + LineOf(code, m.Index) + " " + name
                                        + " 画法=\"" + lit.Groups[1].Value + "\"（只认 margin/fill）");
                            }
                        }
                    }
                }
                Check("扫描③-b 调用点的色名/画法实参全合法（非法色名会让 SetColor 抛 → 整张标注图丢失）",
                    bad.Count == 0 && seen.Count >= 4,
                    bad.Count > 0 ? string.Join(" | ", bad) : "用到=[" + string.Join(",", seen) + "]");

                // (c) 赋值面：给"名字含 Color"的东西赋值时，右边只能由白名单字面量 + 已验常量拼成
                //     （TextColor 这类**属性**也在这里被兜住：它的默认值与唯一一处赋值都要过判）
                bad.Clear();
                foreach (var f in rdFiles)
                {
                    string code = StripComments(Read(f));
                    foreach (Match m in Regex.Matches(code,
                        @"(?<![=!<>])\b(?<n>\w*Color\w*)\s*(?:\{\s*get;\s*set;\s*\}\s*)?=(?!=)\s*(?<rhs>[^;\r\n]+);"))
                    {
                        string rhs = m.Groups["rhs"].Value;
                        foreach (Match lit in Regex.Matches(rhs, @"""([^""]*)"""))
                        {
                            seen.Add(lit.Groups[1].Value);
                            if (!AllowedColors.Contains(lit.Groups[1].Value))
                                bad.Add(Rel(root, f) + ":" + LineOf(code, m.Index) + " " + m.Groups["n"].Value
                                    + " = \"" + lit.Groups[1].Value + "\"");
                        }
                        foreach (Match id in Regex.Matches(rhs, @"\b[A-Za-z_]\w*Color\w*\b"))
                        {
                            if (!colorConsts.ContainsKey(id.Value) && id.Value != m.Groups["n"].Value)
                                bad.Add(Rel(root, f) + ":" + LineOf(code, m.Index) + " " + m.Groups["n"].Value
                                    + " 引用了未经验证的色名来源 " + id.Value);
                        }
                    }
                }
                Check("扫描③-c 色名变量/属性的赋值面只由白名单字面量与已验常量拼成",
                    bad.Count == 0, bad.Count == 0 ? "(无违规)" : string.Join(" | ", bad));
            }

            // ---- ④ OpenWindow 只允许出现在共享件里（守 D9：谁在插件里新开离屏窗就是第六份烘焙件）----
            {
                string allowed = Path.GetFullPath(bakerFile);
                // ★ 豁免清单：只剩一份**永久**例外，清单不许再变长。
                //   HDevDisplayBackend 不是"一次性把计划烧进底图"的烘焙件，而是 HDevEngine 按 dev_* 算子
                //   **逐条驱动**的显示后端：窗口状态是 [ThreadStatic] 的、还要 HandoutWindow 把窗口交给脚本，
                //   "画什么"在脚本跑完之前根本不存在，BakePlan（跑完才攒得出来）表达不了它。
                //   其余四份（BlobDetect / CaliperMeasure / BeadInspect / Matching）已于 2026-10-13 迁入共享件。
                var exempt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    Path.GetFullPath(Path.Combine(root, @"Plugins\Plugin.ImageScript\HDevDisplayBackend.cs")),     // 永久例外（见上）
                };
                var openRe = new Regex(@"\bOpenWindow\s*\(");

                var hits = new List<string>();
                foreach (var f in files)
                {
                    string full = Path.GetFullPath(f);
                    if (string.Equals(full, allowed, StringComparison.OrdinalIgnoreCase)) continue;
                    if (exempt.Contains(full)) continue;
                    string code = StripComments(Read(f));
                    foreach (Match m in openRe.Matches(code))
                        hits.Add(Rel(root, f) + ":" + LineOf(code, m.Index));
                }
                Check("扫描④【D9】全仓 OpenWindow 只出现在 AnnotationBaker.cs + " + exempt.Count + " 份豁免里（清单只许变短）",
                    hits.Count == 0, hits.Count == 0 ? "豁免 " + exempt.Count + " 份" : string.Join(" | ", hits));

                // 反向断言：豁免清单不许过期（文件迁完了还挂在清单里 = 豁免面虚胖）
                var stale = exempt
                    .Where(e => !File.Exists(e) || !openRe.IsMatch(StripComments(Read(e))))
                    .Select(e => Rel(root, e)).ToList();
                Check("扫描④b 豁免清单没有过期项（迁完的文件必须从清单里删掉）",
                    stale.Count == 0, stale.Count == 0 ? "(无)" : string.Join(" | ", stale));

                // 本插件自己绝不能开窗/dump/SetPart（DisplayDraw 的类注释就是这么写的，这条把它变成可执行的）
                var infra = new Regex(@"\bOpenWindow\s*\(|\bDumpWindowImage\s*\(|\bSetPart\s*\(");
                var rdHits = rdFiles.Where(f => infra.IsMatch(StripComments(Read(f)))).Select(f => Rel(root, f)).ToList();
                Check("扫描④c Plugin.ResultDisplay 自己**零**开窗/dump/SetPart（那属于共享件的职责）",
                    rdHits.Count == 0, rdHits.Count == 0 ? "(无)" : string.Join(" | ", rdHits));
            }

            // ---- ⑤ DumpWindowImage 之后 5 行内必须释放源对象（160MB 泄漏那个坑）----
            {
                var hits = new List<string>();
                foreach (var f in files)
                {
                    string code = StripComments(Read(f));
                    var lines = code.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var m = Regex.Match(lines[i], @"DumpWindowImage\s*\(\s*out\s+(?:HObject\s+)?(\w+)");
                        if (!m.Success) continue;
                        string var = m.Groups[1].Value;
                        bool disposed = false;
                        for (int k = i; k < Math.Min(lines.Length, i + 6); k++)
                            if (lines[k].Contains(var + ".Dispose()")) { disposed = true; break; }
                        if (!disposed) hits.Add(Rel(root, f) + ":" + (i + 1) + " 变量 '" + var + "' 未释放");
                    }
                }
                Check("扫描⑤【160MB 泄漏】每个 DumpWindowImage 的源对象在 5 行内 Dispose",
                    hits.Count == 0, hits.Count == 0 ? "(全部已释放)" : string.Join(" | ", hits));
            }

            // ---- ⑥ PublishPreview 的第 2 个实参不许是 `viewIndex + 1` ----
            {
                var hits = new List<string>();
                foreach (var f in files)
                {
                    string code = StripComments(Read(f));
                    foreach (Match m in Regex.Matches(code, @"PublishPreview\s*\("))
                    {
                        var parts = SplitTopLevel(ArgsOf(code, m.Index + m.Length - 1));
                        if (parts.Count < 2) continue;
                        if (Regex.IsMatch(parts[1], @"\+\s*1\b"))
                            hits.Add(Rel(root, f) + ":" + LineOf(code, m.Index) + " 第2实参='" + OneLine(parts[1]) + "'");
                    }
                }
                Check("扫描⑥ PublishPreview 的第 2 个实参没有 `+1`（viewIndex 直传，防'选窗口1图进第2格'）",
                    hits.Count == 0, hits.Count == 0 ? "(无)" : string.Join(" | ", hits));

                // 本插件确实调了 PublishPreview 且第 2 实参就是 viewIndex 语义的变量（不是常量、不是算式）
                string rdCode = string.Concat(rdFiles.Select(f => StripComments(Read(f))));
                var rdCalls = Regex.Matches(rdCode, @"PublishPreview\s*\(")
                    .Select(m => SplitTopLevel(ArgsOf(rdCode, m.Index + m.Length - 1)))
                    .Where(pp => pp.Count >= 2).ToList();
                Check("扫描⑥b 本插件的 PublishPreview 第 2 实参就是 viewIndex 语义的变量",
                    rdCalls.Count >= 1 && rdCalls.All(pp => Regex.IsMatch(pp[1].Trim(), @"^(viewIndex|index)$")),
                    rdCalls.Count == 0 ? "(没有调用)" : string.Join(" | ", rdCalls.Select(pp => "'" + OneLine(pp[1]) + "'")));
            }

            // ---- ⑦ 九宫格锚点：枚举**成员顺序是承重的**（DisplayDraw 用 (int)/3 取带、%3 取列）----
            {
                string anchorFile = Path.Combine(rdDir, "TextAnchorKind.cs");
                string src = File.Exists(anchorFile) ? StripComments(Read(anchorFile)) : "";
                var members = Regex.Matches(src, @"\[\s*Display\s*\(\s*Name\s*=\s*""([^""]*)""\s*\)\s*\]\s*(\w+)\s*=\s*(\d+)")
                    .Select(m => (Cn: m.Groups[1].Value, Name: m.Groups[2].Value, Val: int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)))
                    .ToList();
                var expect = new (string Name, string Cn)[]
                {
                    ("TopLeft", "左上"), ("TopCenter", "中上"), ("TopRight", "右上"),
                    ("MiddleLeft", "左中"), ("MiddleCenter", "居中"), ("MiddleRight", "右中"),
                    ("BottomLeft", "左下"), ("BottomCenter", "中下"), ("BottomRight", "右下"),
                };
                bool orderOk = members.Count == expect.Length &&
                    members.Select(x => x.Name).SequenceEqual(expect.Select(x => x.Name)) &&
                    members.Select(x => x.Cn).SequenceEqual(expect.Select(x => x.Cn)) &&
                    members.Select((x, i) => x.Val == i).All(ok => ok);
                Check("扫描⑦ 锚点枚举九宫格行主序（Top*→Middle*→Bottom*，值 0..8，中文名与位置一致）",
                    orderOk,
                    "实到=[" + string.Join(",", members.Select(x => x.Name + "/" + x.Cn + "=" + x.Val)) + "]");

                // /3 与 %3 是本功能的布局契约：改了这两个算式而没同步枚举顺序 = "选右下却画在左上"
                string ddAll = string.Concat(rdFiles.Select(f => StripComments(Read(f))));
                Check("扫描⑦b DisplayDraw 确实按 (int)Anchor/3 取水平带、%3 取列带（顺序即布局，所以 ⑦ 值得守）",
                    Regex.IsMatch(ddAll, @"\(\s*int\s*\)\s*Anchor\s*/\s*3") &&
                    Regex.IsMatch(ddAll, @"\(\s*int\s*\)\s*Anchor\s*%\s*3"),
                    "(找 /3 与 %3 两处取带/取列)");
            }

            // ---- ⑧ 三个新配置项**接线完整**：属性 → 钩子 → Build 透传 → 视图绑定，缺一条就是静默失效 ----
            {
                string pluginFile = Path.Combine(rdDir, "ResultDisplayPlugin.cs");
                string viewFile = Path.Combine(rdDir, "ResultDisplayView.xaml");
                string ps = File.Exists(pluginFile) ? StripComments(Read(pluginFile)) : "";
                string vx = File.Exists(viewFile) ? Read(viewFile) : "";

                var triples = new (string Prop, string Type)[]
                {
                    ("TextAnchor", "TextAnchorKind"), ("TextMargin", "int"), ("TextScale", "double"),
                };
                var missing = triples
                    .Where(t => !Regex.IsMatch(ps,
                        @"\[\s*StepConfig[^\]]*\]\s*public\s+partial\s+" + t.Type + @"\s+" + t.Prop + @"\s*\{\s*get\s*;\s*set\s*;\s*\}"))
                    .Select(t => t.Prop + " 缺 [StepConfig] partial " + t.Type)
                    .ToList();
                missing.AddRange(triples
                    .Where(t => !ps.Contains("On" + t.Prop + "Changing"))
                    .Select(t => t.Prop + " 缺夹取钩子 On" + t.Prop + "Changing"));
                Check("扫描⑧ 三个配置项都有 [StepConfig] partial 属性与 Changing 钩子（越界值在入口就夹住）",
                    missing.Count == 0, missing.Count == 0 ? "(三项齐全)" : string.Join(" | ", missing));

                // 配置项存在但没传进 Build = 界面能选、图上不动（这类功能最常见的死法）
                var builds = Regex.Matches(ps, @"DisplayDraw\.Build\s*\(")
                    .Select(m => SplitTopLevel(ArgsOf(ps, m.Index + m.Length - 1)))
                    .ToList();
                Check("扫描⑧b 本插件的 DisplayDraw.Build 调用把三个配置项透传进去（第 8/9/10 实参）",
                    builds.Count == 1 && builds[0].Count == 10 &&
                    builds[0][7].Trim() == "TextAnchor" && builds[0][8].Trim() == "TextMargin" &&
                    builds[0][9].Trim() == "TextScale",
                    builds.Count != 1 ? "Build 调用数=" + builds.Count :
                        "实参数=" + builds[0].Count + " 尾三参=[" +
                        string.Join(",", builds[0].Skip(7).Select(s => OneLine(s))) + "]");

                var unbound = triples
                    .Where(t => !Regex.IsMatch(vx, "Binding\\s+" + t.Prop + @"[,\s]"))
                    .Select(t => t.Prop + " 无绑定")
                    .ToList();
                if (!Regex.IsMatch(vx, @"Binding\s+TextAnchors")) unbound.Add("TextAnchors 下拉数据源未绑");
                Check("扫描⑧c 视图把三项都绑上了（锚点下拉的数据源也一起查，漏了就是空下拉）",
                    unbound.Count == 0, unbound.Count == 0 ? "(三项都有绑定)" : string.Join(" | ", unbound));
            }
        }

        // ==================================================================
        //  C5：演示方案端到端（真加载 → 真编译 → 真执行）
        // ==================================================================
        private const string DemoFlow = "结果显示演示";

        private static void DemoSolution()
        {
            Section("[RD-C5] 演示方案「结果显示演示.vms」：加载 → 编译 → 执行（端到端）");

            var root = RepoRoot();
            // A2（2026-10-22，R38）：文件缺失不再硬红 + return（红久了没人看、整段端到端也永远不跑），
            // 改成**可反证的跳过**：期望路径 / 目录下无同名文件 / 补回后自动恢复，三要素由
            // MissingSolutionSamples 统一产出，缺失清单也只维护那一份。
            string vmsPath = root == null ? "" : MissingSolutionSamples.ExpectedPath(root, DemoFlow + ".vms");
            if (root == null || !File.Exists(vmsPath))
            {
                Check($"C5 演示方案（{DemoFlow}.vms）", true,
                    root == null
                        ? "跳过：定位不到仓库根（连期望路径都给不出，属夹具问题，需查 BaseDirectory 上溯四级）"
                        : MissingSolutionSamples.SkipReason(root, DemoFlow + ".vms"));
                return;
            }
            Check($"C5 演示方案（{DemoFlow}.vms）", true, vmsPath);

            string anglePath = Path.Combine(root, "Image", "卡尺测试", "5-角度90.png");
            string circlePath = Path.Combine(root, "Image", "卡尺测试", "4-圆直径200px.png");

            var events = new List<ImageDisplayEvent<HImage>>();
            Action<ImageDisplayEvent<HImage>> sub = e => events.Add(e);
            GlobalEventBus.Subscribe(sub);
            try
            {
                var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
                Check("C5 生产加载器能打开这份方案（$type 白名单放行 Plugin.ResultDisplay.DisplayItemDef）",
                    loaded.Success && loaded.Data != null, loaded.Message);
                if (!loaded.Success || loaded.Data == null) return;

                var solution = loaded.Data;
                var flow = solution.Flows.FirstOrDefault(f => f.FlowName == DemoFlow);
                Check("C5 方案里能找到目标流程「" + DemoFlow + "」", flow != null,
                    "流程=[" + string.Join(",", solution.Flows.Select(f => f.FlowName)) + "]");
                if (flow == null) return;

                var expected = new[]
                {
                    "图像采集_角度图", "ROI_横线", "ROI_竖线", "几何查找_横线", "几何查找_竖线",
                    "几何计算_夹角", "几何计算_交点", "结果显示_角度汇总",
                    "图像采集_圆图", "几何查找_圆", "结果显示_圆汇总",
                };
                Check("C5 流程步骤依次与预期一致（11 步）",
                    flow.Steps.Count == expected.Length && flow.Steps.Select(s => s.StepName).SequenceEqual(expected),
                    "步骤数=" + flow.Steps.Count + ": " + string.Join(" → ", flow.Steps.Select(s => s.StepName)));
                if (flow.Steps.Count != expected.Length) return;

                // ---- .vms 里存的东西读回来是不是活的 ----
                var rd1 = flow.Steps[7] as ActionStep;
                var rd2 = flow.Steps[10] as ActionStep;
                Check("C5 两个结果显示步骤都是 ActionStep 且 PluginTypeName 指向 Plugin.ResultDisplay",
                    rd1 != null && rd2 != null
                    && rd1.PluginTypeName.Contains("Plugin.ResultDisplay")
                    && rd2.PluginTypeName.Contains("Plugin.ResultDisplay"),
                    (rd1?.PluginTypeName ?? "(非 ActionStep)") + " ｜ " + (rd2?.PluginTypeName ?? "(非 ActionStep)"));
                if (rd1 == null || rd2 == null) return;

                Check("C5【动态端口重建时机】结果显示_角度汇总 的 5 条连线全落在 r1..r5（端口名 = 稳定 Id）+ SrcImage",
                    new[] { "SrcImage", "r1", "r2", "r3", "r4", "r5" }.All(k => rd1.LinkedSources.ContainsKey(k)),
                    "linked=[" + string.Join(",", rd1.LinkedSources.Keys) + "]");
                Check("C5 结果显示_圆汇总 的 3 条连线全落在 r1..r3 + SrcImage",
                    new[] { "SrcImage", "r1", "r2", "r3" }.All(k => rd2.LinkedSources.ContainsKey(k)),
                    "linked=[" + string.Join(",", rd2.LinkedSources.Keys) + "]");

                Check("C5 Items 集合从 JSON 还原成了 5 项 / 3 项（不是 JArray、不是空表）",
                    CountItems(rd1) == 5 && CountItems(rd2) == 3,
                    "角度汇总=" + CountItems(rd1) + " 圆汇总=" + CountItems(rd2));
                Check("C5 判定项与 NG 值也还原了（角度汇总判「判定」、圆汇总判「找到」，NG 值都是 NG）",
                    Text(rd1, "JudgeItemLabel") == "判定" && Text(rd2, "JudgeItemLabel") == "找到"
                    && Text(rd1, "NgValue") == "NG" && Text(rd2, "NgValue") == "NG",
                    "judge='" + Text(rd1, "JudgeItemLabel") + "'/'" + Text(rd2, "JudgeItemLabel")
                    + "' ng='" + Text(rd1, "NgValue") + "'/'" + Text(rd2, "NgValue") + "'");

                // 采集步骤指向仓库内真图（.vms 里写的是绝对路径；按仓库根重定位，保证换机也能跑）
                flow.Steps[0].SetInputValue("FilePath", anglePath);
                flow.Steps[8].SetInputValue("FilePath", circlePath);
                Check("C5 前置：两张演示底图都在仓库里",
                    File.Exists(anglePath) && File.Exists(circlePath), anglePath + " ｜ " + circlePath);

                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);
                VariablePersistenceService.Restore(solution, workspace);

                var steps = flow.Steps.ToArray();
                var compiled = new FlowCompiler(workspace).Compile(steps, DemoFlow);
                Check("C5 流程编译通过（端口存在、连线类型匹配 —— object 口对任意上游放行）",
                    compiled.Success,
                    compiled.Success ? "" : string.Join(" | ", compiled.Errors.Select(e => e.Message)));
                if (!compiled.Success || compiled.Data == null) return;

                var session = new FlowSession { FlowName = DemoFlow, ExecutionEngine = compiled.Data };
                foreach (var s in steps) session.Blueprints.Add(s);
                var log = new StubLog();
                var ctx = new ExecutionContext(log, session, workspace, new CancellationTokenSource().Token);

                events.Clear();
                string threw = "";
                try { compiled.Data.Run(ctx); }
                catch (Exception ex) { threw = ex.GetType().Name + ": " + ex.Message; }

                Check("C5 流程执行无异常", threw.Length == 0, threw);
                Check("C5 全部 11 步执行成功（无失败/未执行）",
                    steps.All(s => s.State == StepState.Success),
                    string.Join(" ", steps.Select(s => s.StepName + "=" + s.State))
                    + " ｜ Warn=" + string.Join(" | ", log.Warns)
                    + " ｜ Err=" + string.Join(" | ", log.Errors));

                // ---- ② 收到「结果显示」的事件，窗口号就是配的那个 ----
                var rdEvents = events.Where(e => e.Title == "结果显示").ToList();
                Check("C5 收到两条「结果显示」事件（窗口 1 与窗口 2 各一条）",
                    rdEvents.Count == 2 && rdEvents.Select(e => e.ViewIndex).OrderBy(i => i).SequenceEqual(new[] { 1, 2 }),
                    "结果显示事件=" + rdEvents.Count + " 全部事件=["
                    + string.Join(",", events.Select(e => e.PluginName + "@" + e.ViewIndex)) + "]");

                var e1 = rdEvents.FirstOrDefault(e => e.ViewIndex == 1);
                var e2 = rdEvents.FirstOrDefault(e => e.ViewIndex == 2);

                // ---- ④ infoRows 条数 == 启用结果项数 ----
                Check("C5 infoRows 条数 == 启用结果项数（角度汇总 5 条 / 圆汇总 3 条）",
                    e1?.InfoRows?.Count == 5 && e2?.InfoRows?.Count == 3,
                    "角度汇总=" + (e1?.InfoRows?.Count ?? -1) + " 圆汇总=" + (e2?.InfoRows?.Count ?? -1));
                Check("C5 infoRows 的标签就是结果项的 Label（悬停能看懂是谁）",
                    e1?.InfoRows != null
                    && e1.InfoRows.Select(r => r.Label).SequenceEqual(new[] { "横线", "竖线", "交点", "夹角", "判定" }),
                    e1?.InfoRows == null ? "(无)" : string.Join(",", e1.InfoRows.Select(r => r.Label + "=" + r.Value)));

                // ---- ③ 产物上真有 cyan 几何像素与文字白底框 ----
                if (e1?.Image != null && e1.Image.IsInitialized() && File.Exists(anglePath))
                {
                    bool cyan = HasCyan(e1.Image);
                    Check("C5【几何烧进去了】产物上采得到 cyan（G−R ≥60 的像素 >50 个；底图是灰度图，只可能来自标注）",
                        cyan, "cyan=" + (cyan ? ">50 像素" : "≤50 像素") + " 尺寸=" + SizeOf(e1.Image));

                    HOperatorSet.ReadImage(out HObject baseRaw, anglePath);
                    using var baseImg = new HImage(baseRaw);
                    Check("C5 产物尺寸 == 底图尺寸（烧录不改画幅）",
                        SizeOf(e1.Image) == SizeOf(baseImg), "产物=" + SizeOf(e1.Image) + " 底图=" + SizeOf(baseImg));

                    // 用**差异像素**而不是"白像素数"：对底图极性不敏感（白底黑线还是黑底白线都成立）
                    long diff = DiffAreaInBand(e1.Image, baseImg, 8, 8, 120, 400);
                    Check("C5【文字烧进去了】产物左上角文字带与底图同带的差异像素 >500（白底框 + 判定色字）",
                        diff > 500, "差异像素=" + diff);
                }
                else
                {
                    Check("C5 产物图像可用", false, "(事件里没有可用的图)");
                }

                if (e2?.Image != null && e2.Image.IsInitialized())
                {
                    bool cyan2 = HasCyan(e2.Image);
                    Check("C5 圆汇总的产物上也采得到 cyan（圆查找的几何真烧进去了）",
                        cyan2, "cyan=" + (cyan2 ? ">50 像素" : "≤50 像素") + " 尺寸=" + SizeOf(e2.Image));
                }
                else
                {
                    Check("C5 圆汇总的产物图像可用", false, "(事件里没有可用的图)");
                }
            }
            finally
            {
                GlobalEventBus.Unsubscribe(sub);
            }
        }

        // ==================================================================
        //  辅助：造图 / 采样 / 构造插件与上下文
        // ==================================================================

        /// <summary>纯黑 byte 画布（GenImageConst + GenImageProto 都是复制语义，中间对象即刻释放）</summary>
        private static HImage BlackCanvas(int w, int h)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", w, h);
            HOperatorSet.GenImageProto(proto, out HObject body, 0);
            proto.Dispose();
            var img = new HImage(body);
            body.Dispose();
            return img;
        }

        /// <summary>
        /// 一块矩形内的最大灰度，**逐通道取最大再合并**。
        /// 不能只读第 1 通道：dump_window_image 出三通道图，cyan=(0,255,255) 的红通道恒为 0，
        /// 只读第 1 通道会把"画了 cyan"读成"什么都没画"（断言假绿）。
        /// </summary>
        private static double BandMax(HImage img, int r1, int c1, int r2, int c2)
        {
            HOperatorSet.CountChannels(img, out HTuple ch);
            double max = double.MinValue;
            for (int i = 1; i <= Math.Max(1, ch.I); i++)
                max = Math.Max(max, ChannelMax(img, i, r1, c1, r2, c2));
            return max;
        }

        /// <summary>单通道在指定矩形内的最大灰度（通道号越界时退回第 1 通道）</summary>
        private static double ChannelMax(HImage img, int channel, int r1, int c1, int r2, int c2)
        {
            HOperatorSet.CountChannels(img, out HTuple ch);
            int idx = channel >= 1 && channel <= ch.I ? channel : 1;
            HOperatorSet.GenRectangle1(out HObject rect, r1, c1, r2, c2);
            try
            {
                HOperatorSet.AccessChannel(img, out HObject c, idx);
                try
                {
                    HOperatorSet.MinMaxGray(rect, c, 0, out HTuple _, out HTuple hi, out HTuple _2);
                    return hi[0].D;
                }
                finally { c.Dispose(); }
            }
            finally { rect.Dispose(); }
        }

        /// <summary>整幅图里有没有 cyan（绿−红 ≥60 的像素数 >50）。灰度底图上不可能出现，只可能来自标注</summary>
        private static bool HasCyan(HImage img)
        {
            HOperatorSet.CountChannels(img, out HTuple ch);
            if (ch.I < 3) return false;
            HOperatorSet.AccessChannel(img, out HObject r, 1);
            HOperatorSet.AccessChannel(img, out HObject g, 2);
            try
            {
                HOperatorSet.SubImage(g, r, out HObject diff, 1, 0);      // byte 相减负数自动截到 0
                try
                {
                    HOperatorSet.Threshold(diff, out HObject reg, 60, 255);
                    try
                    {
                        HOperatorSet.AreaCenter(reg, out HTuple area, out _, out _);
                        return area.Length > 0 && area[0].L > 50;
                    }
                    finally { reg.Dispose(); }
                }
                finally { diff.Dispose(); }
            }
            finally { r.Dispose(); g.Dispose(); }
        }

        /// <summary>
        /// 两张图在指定带内的"灰度差 ≥30 的像素数"。两边都先转灰度：产物是三通道、底图可能是单通道，
        /// 不转就没法直接相减。用**差异**而不是"白像素数"，是为了对底图极性不敏感。
        /// </summary>
        private static long DiffAreaInBand(HImage product, HImage baseImg, int r1, int c1, int r2, int c2)
        {
            if (product == null || baseImg == null) return -1;
            if (SizeOf(product) != SizeOf(baseImg)) return -1;
            HObject gp = ToGray(product), gb = ToGray(baseImg);
            try
            {
                HOperatorSet.AbsDiffImage(gp, gb, out HObject diff, 1);
                try
                {
                    HOperatorSet.Threshold(diff, out HObject hot, 30, 255);
                    try
                    {
                        HOperatorSet.GenRectangle1(out HObject band, r1, c1, r2, c2);
                        try
                        {
                            HOperatorSet.Intersection(hot, band, out HObject common);
                            try
                            {
                                HOperatorSet.AreaCenter(common, out HTuple area, out _, out _);
                                return area.Length > 0 ? area[0].L : 0;
                            }
                            finally { common.Dispose(); }
                        }
                        finally { band.Dispose(); }
                    }
                    finally { hot.Dispose(); }
                }
                finally { diff.Dispose(); }
            }
            finally { gp.Dispose(); gb.Dispose(); }
        }

        /// <summary>转灰度（单通道也拷一份，调用方一律 Dispose，省去"这个句柄是不是我自己的"判断）</summary>
        private static HObject ToGray(HImage img)
        {
            HOperatorSet.CountChannels(img, out HTuple ch);
            if (ch.I >= 3) { HOperatorSet.Rgb1ToGray(img, out HObject g); return g; }
            HOperatorSet.CopyImage(img, out HObject copy);
            return copy;
        }

        /// <summary>BitmapSource 全图最大字节值（Bgr24 三字节 / Gray8 一字节都适用：逐字节取最大就是"最亮"）</summary>
        private static byte MaxOf(BitmapSource bmp)
        {
            int bytesPerPixel = Math.Max(1, bmp.Format.BitsPerPixel / 8);
            int stride = bmp.PixelWidth * bytesPerPixel;
            var buf = new byte[stride * bmp.PixelHeight];
            bmp.CopyPixels(buf, stride, 0);
            byte max = 0;
            foreach (var b in buf) if (b > max) max = b;
            return max;
        }

        private static (int W, int H) SizeOf(HImage img)
        {
            if (img == null || !img.IsInitialized()) return (-1, -1);
            HOperatorSet.GetImageSize(img, out HTuple w, out HTuple h);
            return (w.I, h.I);
        }

        private static bool Eq(double a, double b, double tol = 1e-6) => Math.Abs(a - b) <= tol;

        private static string Fmt(double[] p) =>
            string.Join(",", p.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)));

        private static string OneLine(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

        /// <summary>一个结果项的 Build（默认启用叠字、不判定、预算 300）</summary>
        private static DisplayDrawResult Build(object? value, int w, int h, bool showText = true)
        {
            var items = new List<DisplayItem>
            {
                new(new DisplayItemDef { Id = "r1", Label = "项", Enabled = true }, value)
            };
            return DisplayDraw.Build(items, w, h, showText, "", "NG", 300);
        }

        /// <summary>混合一景：一个圆（cyan）+ 一个数值项（判定色）+ 一个判定项（按 judgeValue 决定红绿）</summary>
        private static DisplayDrawResult BuildMixed(int w, int h, string judge, string ng, object? judgeValue)
        {
            var items = new List<DisplayItem>
            {
                new(new DisplayItemDef { Id = "r1", Label = "圆", Enabled = true }, GeometryParam.Circle(200, 160, 60)),
                new(new DisplayItemDef { Id = "r2", Label = "宽度", Enabled = true, Format = "F2", Unit = "mm" }, 12.34),
                new(new DisplayItemDef { Id = "r3", Label = judge, Enabled = true }, judgeValue),
            };
            return DisplayDraw.Build(items, w, h, true, judge, ng, 300);
        }

        private static (string Id, object Value, string Format, string Unit) It(
            string id, object value, string format = "", string unit = "") => (id, value, format, unit);

        /// <summary>
        /// 造一个插件：底图接好、窗口号设好、结果项逐条建好并灌值。
        /// ★ 灌完值之后**不许**再调 <c>RebuildDynamicInputs()</c> —— 重建端口会 new 出全新的
        /// <c>InputPort&lt;object&gt;</c>，刚灌进去的值随旧端口一起没了（断言会莫名其妙地全绿或全红）。
        /// </summary>
        private static ResultDisplayPlugin NewPlugin(
            int viewIndex, HImage src, params (string Id, object Value, string Format, string Unit)[] items)
        {
            var p = new ResultDisplayPlugin { InstanceName = "结果显示断言" };
            p.Items.Clear();
            foreach (var it in items)
                p.Items.Add(new DisplayItemDef
                {
                    Id = it.Id,
                    Label = "项" + it.Id,
                    Format = it.Format,
                    Unit = it.Unit,
                    Enabled = true
                });
            p.RebuildDynamicInputs();                  // 项全建好了再一次性建端口
            foreach (var it in items)
                p.GetInputPort(it.Id)!.Value = it.Value;
            p.DisplayViewIndex = viewIndex;
            p.SrcImage.Value = src;
            return p;
        }

        private static (ExecutionContext Ctx, StubLog Log) NewContext(string flowName = "结果显示断言流程")
        {
            var workspace = new WorkspaceContext();
            var session = new FlowSession { FlowName = flowName };
            var log = new StubLog();
            return (new ExecutionContext(log, session, workspace, new CancellationTokenSource().Token), log);
        }

        /// <summary>非 IConvertible 的自定义模型：A8 要用它验"认不出的类型只丢自己"</summary>
        private sealed class WeirdModel
        {
            public string Name { get; set; } = "怪东西";
            public override string ToString() => "WeirdModel(" + Name + ")";
        }

        /// <summary>只用来当发布方（C3 的纯黑对照帧）：PublishPreview 只读它的 InstanceName</summary>
        private sealed class PubStub : VisionPluginBase
        {
            public override void RunAlgorithm(IExecutionContext context) { }
        }

        /// <summary>会真实存链接/常量的 StepData 桩（范式：BeadInspectChecks.LinkableFakeStepData）</summary>
        private sealed class LinkableFakeStepData : IStepConfigData
        {
            private readonly Dictionary<string, LinkReference> _links = new();
            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon { get; } = "";
            public string StepName { get; } = "ResultDisplayFake";
            public string Description { get; } = "";
            public Dictionary<string, object> InputValues { get; } = new();
            public void SetInputValue(string key, object value) => InputValues[key] = value;
            public void RemoveInputValue(string key) => InputValues.Remove(key);
            public bool IsLinked(string inputPortName) => _links.ContainsKey(inputPortName);
            public string GetLinkedAddress(string inputPortName) =>
                _links.TryGetValue(inputPortName, out var l) ? l.DisplayAddress : null;
            public LinkReference GetLink(string inputPortName) =>
                _links.TryGetValue(inputPortName, out var l) ? l : null;
            public void SetLink(string inputPortName, LinkReference link) => _links[inputPortName] = link;
            public void RemoveLink(string inputPortName) => _links.Remove(inputPortName);
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
        }

        /// <summary>把 InputValues 摊平成可读串（键=值类型/字面量），断言失败时直接看得出落盘了什么</summary>
        private static string DumpValues(LinkableFakeStepData fake) =>
            string.Join(" | ", fake.InputValues.Select(kv => kv.Key + "=" + (kv.Value switch
            {
                null => "(null)",
                string s => "'" + s + "'",
                _ => kv.Value.GetType().Name
            })));

        // ---- C5 用的模型侧读取辅助 ----

        /// <summary>从 ActionStep.InputValues 里数 Items 集合的元素个数（还原成了真集合才数得出，JArray 也数得出）</summary>
        private static int CountItems(ActionStep step)
            => step.InputValues.TryGetValue("Items", out var v) && v is System.Collections.ICollection c ? c.Count : -1;

        private static string Text(ActionStep step, string key)
            => step.InputValues.TryGetValue(key, out var v) && v is string s ? s : "(无)";

        // ---- 静态扫描辅助 ----

        /// <summary>仓库根（以 AGENTS.md 为锚；范式 VariableBindingCheck.RepoRoot）</summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
            return null;
        }

        private static bool IsBuildDir(string file) =>
            Regex.IsMatch(file, @"\\(bin|obj)\\", RegexOptions.IgnoreCase);

        private static string Read(string file) => File.ReadAllText(file);

        private static string Rel(string root, string file) =>
            file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? file.Substring(root.Length).TrimStart('\\', '/')
                : file;

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>
        /// 去掉注释但**保留行数与列偏移**（行号要指向原文件）。
        /// 必须去注释：DisplayDraw.cs 的类注释里就写着"本文件不许出现 OpenWindow / DumpWindowImage / SetPart"，
        /// 不去注释这条纪律自己就会判自己红。
        /// </summary>
        private static string StripComments(string text)
        {
            string s = Regex.Replace(text, @"/\*.*?\*/",
                m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
            return Regex.Replace(s, @"//[^\r\n]*", "");
        }

        /// <summary>从 <paramref name="openParenIndex"/>（指向 '('）起取配平的实参串（不含最外层括号）</summary>
        private static string ArgsOf(string code, int openParenIndex)
        {
            int depth = 0;
            var sb = new StringBuilder();
            for (int i = openParenIndex; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '(') { depth++; if (depth == 1) continue; }
                else if (c == ')') { depth--; if (depth == 0) break; }
                if (depth >= 1) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 按**顶层**逗号切实参。深度要同时数 ()、[]、{}（`new[] { g.Row, g.Col, size }` 里的逗号
        /// 不是分隔符——漏数花括号会让"色名在第 3 个实参"这类判据整体错位）；
        /// 字符串/字符字面量整段跳过（`"{"`、`'{',` 这种字面量里的括号与逗号都不算语法）。
        /// </summary>
        private static List<string> SplitTopLevel(string args)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;
            bool inStr = false, verbatim = false, inChar = false;
            for (int i = 0; i < args.Length; i++)
            {
                char c = args[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '"')
                    {
                        if (verbatim && i + 1 < args.Length && args[i + 1] == '"') sb.Append(args[++i]);
                        else inStr = false;
                    }
                    else if (!verbatim && c == '\\' && i + 1 < args.Length) sb.Append(args[++i]);
                    continue;
                }
                if (inChar)
                {
                    sb.Append(c);
                    if (c == '\\') { if (i + 1 < args.Length) sb.Append(args[++i]); }
                    else if (c == '\'') inChar = false;
                    continue;
                }
                if (c == '"') { inStr = true; verbatim = i > 0 && args[i - 1] == '@'; sb.Append(c); continue; }
                if (c == '\'') { inChar = true; sb.Append(c); continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                if (c == ',' && depth == 0) { parts.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(c);
            }
            parts.Add(sb.ToString());
            return parts;
        }
    }
}
