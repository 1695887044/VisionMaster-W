using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Core.Halcon.Helpers;
using HalconDotNet;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 共享离屏烘焙件 <see cref="AnnotationBaker"/> 的契约断言（B1~B8）。
    ///
    /// 为什么这一组断言是本轮最重要的新增：仓库此前有**五份**离屏烘焙实现，
    /// <c>FlowCanvasChecks</c> 对它们**零断言覆盖**（grep AnnotationRenderer / RenderAnnotated /
    /// OffscreenRenderer / DumpWindowImage 全零命中）。也就是说"反复开关窗口 #9302 死锁"
    /// "dump 源对象不释放漏 160MB""part 撑大导致底图缩角落"这三个已经付过学费的坑，
    /// 全靠注释在守——注释不会被 CI 跑。这一组把三个坑都变成像素级/内存级的可执行断言。
    ///
    /// 手法：造已知画布（<c>GenImageConst</c> + <c>PaintRegion</c>）→ 烘焙 → 采样像素
    /// （沿用 <c>YoloChecks</c>/<c>CaliperMeasureChecks</c> 的既有范式）。
    /// </summary>
    internal static class AnnotationBakerChecks
    {
        public static void Run()
        {
            Section("[BAKE] 共享离屏烘焙件 AnnotationBaker（六条硬纪律的回归网）");
            B1_CanvasFidelity();
            B2_TextBox();
            B3_NoResidueAcrossRounds();
            B4_NonByteMonoNotDeadWhite();
            B5_FourChannelNoThrow();
            B6_BadElementsDegradeNotThrow();
            B7_NoLeakAndDisposeSafety();
            B8_TextHorizontalAlign();
        }

        // ==================================================================
        //  B1：画布保真 —— 产物尺寸 == 底图尺寸、四角是底色不是黑边、
        //      底图右下角的"指纹块"仍在原位（底图缩在左上角时它就没了）
        // ==================================================================
        private static void B1_CanvasFidelity()
        {
            const int W = 200, H = 100;
            var baker = new AnnotationBaker("black", 20);
            var warns = new List<string>();
            HImage baseImg = null!, product = null!, mono = null!;
            try
            {
                // 灰底 100 + 右下角一块 180 的指纹（底图若被缩进左上角，这块必然错位或消失）
                baseImg = GrayCanvas(W, H, 100);
                var fingerprint = PaintRect(baseImg, 70, 140, 85, 175, 180);
                baseImg.Dispose();
                baseImg = fingerprint;

                var plan = new BakePlan();
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Line,
                    new double[] { 50, 40, 50, 160 }, "white", 2));

                product = baker.Bake(baseImg, plan, warns.Add)!;
                Check("B1 烘焙产出非 null 且尺寸 == 底图尺寸（200x100）",
                    product != null && SizeOf(product) == (W, H),
                    product == null ? $"返回 null；warns=[{string.Join(" | ", warns)}]" : $"{SizeOf(product)}");
                if (product == null) return;

                mono = ToMono(product);

                // 四角必须是底图灰度 100：窗口底色是 black，
                // 只要 SetPart 没生效（part 被撑大 → 底图缩角落）四角就会是 0
                double c00 = Sample(mono, 0, 0), c0w = Sample(mono, 0, W - 1);
                double ch0 = Sample(mono, H - 1, 0), chw = Sample(mono, H - 1, W - 1);
                Check("B1【纪律3 SetPart】四角像素 == 底图灰度 100（不是窗口黑底、底图没缩进左上角）",
                    Near(c00, 100) && Near(c0w, 100) && Near(ch0, 100) && Near(chw, 100),
                    $"(0,0)={c00} (0,{W - 1})={c0w} ({H - 1},0)={ch0} ({H - 1},{W - 1})={chw}");

                Check("B1 底图右下角指纹块仍在原位（灰度 ≈180）",
                    Near(Sample(mono, 78, 160), 180), $"(78,160)={Sample(mono, 78, 160)}");

                var onLine = StatsOf(mono, 47, 90, 53, 110);
                Check("B1 白色横线真的烧进了像素（行50 附近最大值 >200）",
                    onLine.Max > 200, $"band(47..53, 90..110) min={onLine.Min} max={onLine.Max}");

                var offLine = StatsOf(mono, 5, 5, 15, 60);
                Check("B1 没画的地方仍是底图（行5..15 最大值 ≤130，没有莫名笔画）",
                    offLine.Max <= 130, $"band(5..15, 5..60) max={offLine.Max}");
            }
            finally
            {
                mono?.Dispose(); product?.Dispose(); baseImg?.Dispose(); baker.Dispose();
            }
        }

        // ==================================================================
        //  B2：文字底色框 —— 仓库里有一处**互相矛盾的实测记录**，用像素给最终裁决
        //    · Caliper\AnnotationRenderer.cs:94-95 断言"只给 box_color 画不出框，必须同时给 box,true"
        //    · ImageScript\HDevDisplayBackend.cs:514-518 量化实测"box 默认就是开的（18303px vs 3754px）"
        //  AnnotationBaker 一律显式传 box，两种解释下都正确；本断言钉住"显式 true 真有框、显式 false 真没框"
        // ==================================================================
        private static void B2_TextBox()
        {
            const int W = 240, H = 120;
            var baker = new AnnotationBaker("black", 20);
            HImage baseImg = null!, withBox = null!, noBox = null!, m1 = null!, m2 = null!;
            try
            {
                baseImg = GrayCanvas(W, H, 100);

                var p1 = new BakePlan();
                p1.Texts.Add(new BakeText("ABC 123", 24, 24, "black", 20, true));
                withBox = baker.Bake(baseImg, p1, null)!;

                var p2 = new BakePlan();
                p2.Texts.Add(new BakeText("ABC 123", 24, 24, "black", 20, false));
                noBox = baker.Bake(baseImg, p2, null)!;

                Check("B2 两次烘焙（带框 / 不带框）都产出非 null",
                    withBox != null && noBox != null,
                    $"withBox={(withBox != null)} noBox={(noBox != null)}");
                if (withBox == null || noBox == null) return;

                m1 = ToMono(withBox);
                m2 = ToMono(noBox);

                // 文字锚点 (24,24)、字号 20 → 文字与底色框都落在这一带
                var s1 = StatsOf(m1, 14, 14, 60, 170);
                var s2 = StatsOf(m2, 14, 14, 60, 170);

                Check("B2【纪律5 box】显式 Box:true → 文字周围采到接近白的底色框像素（max ≥240）",
                    s1.Max >= 240, $"带框 band min={s1.Min} max={s1.Max}");

                Check("B2【纪律5 box】显式 Box:false → 同一带内没有白框（max ≤130，只有黑字与灰底）",
                    s2.Max <= 130, $"不带框 band min={s2.Min} max={s2.Max}");

                Check("B2 黑字确实落墨（不带框时 band 最小值 <60）",
                    s2.Min < 60, $"不带框 band min={s2.Min}");
            }
            finally
            {
                m1?.Dispose(); m2?.Dispose(); withBox?.Dispose(); noBox?.Dispose();
                baseImg?.Dispose(); baker.Dispose();
            }
        }

        // ==================================================================
        //  B3：跨轮无残留 —— 窗口按尺寸常驻复用（纪律2），
        //      第 3 轮的产物上不许出现第 1、2 轮画过的线
        // ==================================================================
        private static void B3_NoResidueAcrossRounds()
        {
            const int W = 200, H = 100;
            var baker = new AnnotationBaker();
            HImage baseImg = null!, r1 = null!, r2 = null!, r3 = null!, m3 = null!;
            try
            {
                baseImg = GrayCanvas(W, H, 100);

                r1 = baker.Bake(baseImg, LineAt(20), null);
                r2 = baker.Bake(baseImg, LineAt(50), null);
                r3 = baker.Bake(baseImg, LineAt(80), null);

                Check("B3【纪律2 窗口复用】同尺寸连烘 3 轮都成功（没有反复开关窗口）",
                    r1 != null && r2 != null && r3 != null,
                    $"r1={r1 != null} r2={r2 != null} r3={r3 != null}");
                if (r3 == null) return;

                m3 = ToMono(r3);
                var own = StatsOf(m3, 77, 40, 83, 160);
                var old1 = StatsOf(m3, 17, 40, 23, 160);
                var old2 = StatsOf(m3, 47, 40, 53, 160);

                Check("B3 第 3 轮自己的线在（行80 附近 max >200）", own.Max > 200,
                    $"band(77..83) max={own.Max}");
                Check("B3【纪律3 串轮污染】第 1 轮的线没有残留（行20 附近 max ≤130）", old1.Max <= 130,
                    $"band(17..23) max={old1.Max}");
                Check("B3【纪律3 串轮污染】第 2 轮的线没有残留（行50 附近 max ≤130）", old2.Max <= 130,
                    $"band(47..53) max={old2.Max}");
            }
            finally
            {
                m3?.Dispose(); r1?.Dispose(); r2?.Dispose(); r3?.Dispose();
                baseImg?.Dispose(); baker.Dispose();
            }
        }

        // ==================================================================
        //  B4：非 byte 单通道底图不许整片死白
        //    convert_image_type(...,'byte') 是**截断/饱和**不是缩放（uint2 灰度 1632 → 255），
        //    所以必须先按实际灰度范围线性拉伸。Caliper 那份归一化缺这一步 → 12/16 位相机整片死白
        // ==================================================================
        private static void B4_NonByteMonoNotDeadWhite()
        {
            const int W = 200, H = 100;
            var baker = new AnnotationBaker();
            HImage u2 = null!, product = null!, mono = null!;
            try
            {
                // uint2：整幅 1000，右下角一块 2000。两个值都远超 255 → 不拉伸就是全 255 死白
                HOperatorSet.GenImageConst(out HObject proto, "uint2", W, H);
                HOperatorSet.GenImageProto(proto, out HObject body, 1000);
                proto.Dispose();
                u2 = new HImage(body);
                body.Dispose();

                var bright = PaintRect(u2, 60, 100, 90, 190, 2000);
                u2.Dispose();
                u2 = bright;

                HOperatorSet.GetImageType(u2, out HTuple tIn);
                HOperatorSet.CountChannels(u2, out HTuple chIn);
                Check("B4 前置：底图确实是 uint2 单通道（否则本断言没意义）",
                    tIn.S == "uint2" && chIn.I == 1, $"type={tIn.S} channels={chIn.I}");

                product = baker.Bake(u2, new BakePlan(), null)!;
                Check("B4 非 byte 单通道底图能烘焙（产出非 null、尺寸不变）",
                    product != null && SizeOf(product) == (W, H),
                    product == null ? "返回 null" : $"{SizeOf(product)}");
                if (product == null) return;

                mono = ToMono(product);
                var all = StatsOf(mono, 0, 0, H - 1, W - 1);
                Check("B4【死白陷阱】产物有灰度层次（全图 range >100，不是整片 255）",
                    all.Range > 100, $"全图 min={all.Min} max={all.Max} range={all.Range}");

                double dark = Sample(mono, 10, 10), light = Sample(mono, 75, 150);
                Check("B4 两块灰度可区分（1000 的那块明显暗于 2000 的那块）",
                    light - dark > 60, $"暗块(10,10)={dark} 亮块(75,150)={light}");
            }
            finally
            {
                mono?.Dispose(); product?.Dispose(); u2?.Dispose(); baker.Dispose();
            }
        }

        // ==================================================================
        //  B5：4 通道底图（RGBA/BGRA）不许抛 —— disp_obj 只稳妥支持 1/3 通道，
        //      直接画会报"通道数不对"，整张标注图就没了 → 必须先丢 alpha
        // ==================================================================
        private static void B5_FourChannelNoThrow()
        {
            const int W = 160, H = 80;
            var baker = new AnnotationBaker();
            var warns = new List<string>();
            HImage rgba = null!, product = null!;
            var temps = new List<HObject>();
            try
            {
                rgba = Build4Channel(W, H, temps);
                HOperatorSet.CountChannels(rgba, out HTuple ch);
                Check("B5 前置：底图确实是 4 通道", ch.I == 4, $"channels={ch.I}");

                var plan = new BakePlan();
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Line, new double[] { 40, 20, 40, 140 }, "cyan", 2));
                plan.Texts.Add(new BakeText("4ch", 10, 10, "white", 16, true));

                bool threw = false;
                try { product = baker.Bake(rgba, plan, warns.Add); }
                catch (Exception ex) { threw = true; warns.Add("抛异常：" + ex.Message); }

                Check("B5 4 通道底图烘焙不抛异常", !threw, threw ? warns[^1] : "");
                Check("B5 4 通道底图仍能产出标注图（尺寸不变）",
                    product != null && SizeOf(product) == (W, H),
                    product == null ? $"返回 null；warns=[{string.Join(" | ", warns)}]" : $"{SizeOf(product)}");
                Check("B5 4 通道没有触发任何降级告警", warns.Count == 0,
                    warns.Count == 0 ? "(无告警)" : string.Join(" | ", warns));
            }
            finally
            {
                product?.Dispose(); rgba?.Dispose();
                foreach (var o in temps) { try { o.Dispose(); } catch { } }
                baker.Dispose();
            }
        }

        // ==================================================================
        //  B6：坏元素只丢自己，不丢整张图，也不抛异常（宁缺显示不停线）
        // ==================================================================
        private static void B6_BadElementsDegradeNotThrow()
        {
            const int W = 200, H = 100;
            var baker = new AnnotationBaker();
            var warns = new List<string>();
            HImage baseImg = null!, product = null!, mono = null!;
            try
            {
                baseImg = GrayCanvas(W, H, 100);

                var plan = new BakePlan();
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Line,
                    new double[] { 30, 40, 30, 160 }, "notacolor", 2));            // 非法色名 → 回落 green
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Line,
                    new double[] { double.NaN, 40, 55, 160 }, "white", 2));        // NaN → 丢
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Circle,
                    new double[] { 70, 100, -5 }, "white", 2));                    // 负半径 → 丢
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Line,
                    new double[] { 45, 4, 45, 6 }, "white", 2));                   // 参数个数够但太短，照画
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Line,
                    new double[] { 85, 40, 85 }, "white", 2));                     // 参数个数非法 → 丢
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Line,
                    new double[] { 70, 40, 70, 160 }, "white", 2));                // 好元素 → 必须还在
                plan.Texts.Add(new BakeText("", 10, 10, "white", 16, true));       // 空文本 → 静默跳过
                plan.Objects.Add(new BakeObj(null!, "white", 1, "margin"));         // 未初始化对象 → 丢

                bool threw = false;
                try { product = baker.Bake(baseImg, plan, warns.Add); }
                catch (Exception ex) { threw = true; warns.Add("抛异常：" + ex.Message); }

                Check("B6 非法色名 / NaN / 负半径 / 参数个数错 / 空对象 全都不抛异常", !threw,
                    threw ? warns[^1] : $"warns={warns.Count} 条");
                Check("B6 仍然产出标注图（坏元素只丢自己，不丢整张）",
                    product != null && SizeOf(product) == (W, H),
                    product == null ? "返回 null" : $"{SizeOf(product)}");
                if (product == null) return;

                mono = ToMono(product);
                var good = StatsOf(mono, 67, 40, 73, 160);
                Check("B6 好元素照常烧进像素（行70 白线 max >200）", good.Max > 200,
                    $"band(67..73) max={good.Max}");

                string joined = string.Join(" | ", warns);
                Check("B6 非法色名有告警且明确回落 green（绝不把用户字符串当色名传下去）",
                    joined.Contains("非法色名") && joined.Contains("green"), joined);
                Check("B6 非有限坐标有告警（NaN 被丢时留痕，不是静默）",
                    joined.Contains("非有限值"), joined);
                Check("B6 负半径有告警", joined.Contains("半径非法"), joined);
                Check("B6 参数个数非法有告警", joined.Contains("参数个数非法"), joined);
            }
            finally
            {
                mono?.Dispose(); product?.Dispose(); baseImg?.Dispose(); baker.Dispose();
            }
        }

        // ==================================================================
        //  B7：500 轮不泄漏 + Dispose 后不抛
        //    钉住的是"dump_window_image 的源对象不释放 → 500 次约漏 160MB"那个已付过学费的坑
        // ==================================================================
        private static void B7_NoLeakAndDisposeSafety()
        {
            const int W = 640, H = 480, Rounds = 500;
            var baker = new AnnotationBaker();
            var warns = new List<string>();
            HImage baseImg = null!;
            try
            {
                baseImg = GrayCanvas(W, H, 90);
                var plan = new BakePlan();
                plan.Shapes.Add(new BakeShape(BakeShapeKind.Circle, new double[] { 240, 320, 120 }, "cyan", 2));
                plan.Texts.Add(new BakeText("泄漏探针 500 轮", 20, 20, "white", 20, true));

                for (int i = 0; i < 20; i++) baker.Bake(baseImg, plan, null)?.Dispose();   // 预热

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var proc = Process.GetCurrentProcess();
                proc.Refresh();
                long before = proc.PrivateMemorySize64;

                int ok = 0;
                for (int i = 0; i < Rounds; i++)
                {
                    var p = baker.Bake(baseImg, plan, warns.Add);
                    if (p != null) { ok++; p.Dispose(); }
                }

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                proc.Refresh();
                long after = proc.PrivateMemorySize64;
                double deltaMB = (after - before) / 1024.0 / 1024.0;

                Check($"B7 {Rounds} 轮烘焙全部成功（窗口常驻复用，没有反复开关）",
                    ok == Rounds, $"成功 {ok}/{Rounds}");

                // 若不释放 dump 源对象：500 × 640×480 ≈ 150MB（单通道）~ 460MB（三通道），
                // 阈值 60MB 足以区分"释放了"与"没释放"，又不会被 HALCON 自身的内存缓存抖动误伤
                Check($"B7【纪律6 160MB 泄漏】{Rounds} 轮后私有内存增量 <60MB",
                    deltaMB < 60, $"增量 {deltaMB:0.#} MB（{before / 1024 / 1024}MB → {after / 1024 / 1024}MB）");
                Check("B7 500 轮里没有触发任何降级告警", warns.Count == 0,
                    warns.Count == 0 ? "(无告警)" : string.Join(" | ", warns));

                baker.Dispose();
                var late = new List<string>();
                HImage? post = null;
                bool threw = false;
                try { post = baker.Bake(baseImg, plan, late.Add); }
                catch (Exception ex) { threw = true; late.Add(ex.Message); }
                Check("B7 Dispose 后再 Bake：返回 null、不抛、且有告警留痕",
                    !threw && post == null && late.Count > 0,
                    threw ? $"抛了：{late[0]}" : $"null={post == null} warns=[{string.Join(" | ", late)}]");
                post?.Dispose();

                baker.Dispose();   // 幂等：第二次 Dispose 不许抛
                Check("B7 Dispose 幂等（连调两次不抛）", true, "");
            }
            finally
            {
                baseImg?.Dispose();
                baker.Dispose();
            }
        }

        // ==================================================================
        //  B8：文字水平对齐（HAlign）—— 「结果显示」九宫格锚点的承重件
        //
        //  为什么对齐必须落在烘焙件、断言也必须落在像素：文字宽度取决于**窗口当前字体**，
        //  调用方（DisplayDraw）拿不到窗口、量不了宽度；而"右下对齐看着没对齐"这种缺陷
        //  在 plan 层面根本看不出来（col 是个数字，含义取决于烘焙件怎么解释它）。
        //  所以这里用墨迹包围盒（threshold → smallest_rectangle1）直接问像素。
        //
        //  七条：① 四种对齐都产出非 null；② 墨迹可测；③ 不写 HAlign 与显式 Left 逐像素同界
        //  （= 四份已迁插件零行为变化的证据）；④ 三种对齐墨迹同宽（换的是锚点含义，不是内容/字号）；
        //  ⑤ Left 墨迹左缘贴给定列 / ⑥ Right 墨迹右缘贴给定列 / ⑦ Center 墨迹中心落给定列（各 ±10px，
        //  只有实测宽度而非等宽估算才可能这么近）；另加"整块右移 ≥100px"与"垂直不受影响"两道对照。
        // ==================================================================
        private static void B8_TextHorizontalAlign()
        {
            const int W = 400, H = 120;
            const string Text = "ABCDEFGH";
            const int Font = 20;
            var baker = new AnnotationBaker("black", Font);
            HImage baseImg = null!;
            var products = new List<HImage>();
            var monos = new List<HImage>();
            try
            {
                baseImg = GrayCanvas(W, H, 100);   // 墨迹=黑字，底图均匀灰 100 → 阈值 0..60 只吃字

                // 默认（不写 HAlign）必须与显式 Left 一模一样：四份已迁插件的调用点没有这一项
                var pDefault = new BakePlan();
                pDefault.Texts.Add(new BakeText(Text, 40, 20, "black", Font, false));
                var pLeft = new BakePlan();
                pLeft.Texts.Add(new BakeText(Text, 40, 20, "black", Font, false) { HAlign = BakeHAlign.Left });
                var pCenter = new BakePlan();
                pCenter.Texts.Add(new BakeText(Text, 40, W / 2, "black", Font, false) { HAlign = BakeHAlign.Center });
                var pRight = new BakePlan();
                pRight.Texts.Add(new BakeText(Text, 40, W - 20, "black", Font, false) { HAlign = BakeHAlign.Right });

                var imgs = new List<HImage>();
                foreach (var p in new[] { pDefault, pLeft, pCenter, pRight })
                {
                    var got = baker.Bake(baseImg, p, null);
                    imgs.Add(got!);
                    if (got != null) products.Add(got);
                }
                bool allBaked = imgs.TrueForAll(i => i != null);
                Check("B8 四种对齐（默认/Left/Center/Right）都产出非 null", allBaked,
                    "baked=" + string.Join("/", imgs.Select(i => i != null ? "y" : "n")));
                if (!allBaked) return;

                foreach (var i in imgs) monos.Add(ToMono(i));
                var dflt = InkBox(monos[0]);
                var left = InkBox(monos[1]);
                var center = InkBox(monos[2]);
                var right = InkBox(monos[3]);

                Check("B8 墨迹可测（四种对齐都阈值出非空包围盒）",
                    dflt != null && left != null && center != null && right != null,
                    $"默认={Fmt(dflt)} left={Fmt(left)} center={Fmt(center)} right={Fmt(right)}");
                if (dflt == null || left == null || center == null || right == null) return;

                Check("B8【零行为变化】不写 HAlign 与显式 Left 的墨迹包围盒一致（±4px）——四份已迁插件不受影响",
                    Math.Abs(dflt.Value.C1 - left.Value.C1) <= 4 && Math.Abs(dflt.Value.C2 - left.Value.C2) <= 4,
                    $"默认 c={dflt.Value.C1}..{dflt.Value.C2}，Left c={left.Value.C1}..{left.Value.C2}");

                int wLeft = left.Value.C2 - left.Value.C1;
                Check("B8 三种对齐墨迹同宽（换的是锚点含义，不是内容或字号）",
                    Math.Abs(center.Value.C2 - center.Value.C1 - wLeft) <= 6 &&
                    Math.Abs(right.Value.C2 - right.Value.C1 - wLeft) <= 6,
                    $"宽 left={wLeft} center={center.Value.C2 - center.Value.C1} right={right.Value.C2 - right.Value.C1}");

                Check("B8【Left】墨迹左缘 == 给定列 20（±10px）",
                    Math.Abs(left.Value.C1 - 20) <= 10, $"c1={left.Value.C1}");
                Check($"B8【Right】墨迹右缘 == 给定列 {W - 20}（±10px，实测宽度才可能这么近）",
                    Math.Abs(right.Value.C2 - (W - 20)) <= 10, $"c2={right.Value.C2}，期望≈{W - 20}，字宽≈{wLeft}");
                Check("B8【Center】墨迹中心 == 给定列 200（±10px）",
                    Math.Abs((center.Value.C1 + center.Value.C2) / 2.0 - W / 2.0) <= 10,
                    $"中心={(center.Value.C1 + center.Value.C2) / 2.0:0.#}，c={center.Value.C1}..{center.Value.C2}");

                Check("B8【Right 整块右移】Right 墨迹左缘比 Left 至少右移 100px",
                    right.Value.C1 - left.Value.C1 >= 100,
                    $"left c1={left.Value.C1} → right c1={right.Value.C1}");

                Check("B8 垂直不受对齐影响（三种对齐墨迹上缘同行带，±6px）",
                    Math.Abs(left.Value.R1 - right.Value.R1) <= 6 && Math.Abs(left.Value.R1 - center.Value.R1) <= 6,
                    $"r1 left={left.Value.R1} center={center.Value.R1} right={right.Value.R1}");
            }
            finally
            {
                foreach (var m in monos) m?.Dispose();
                foreach (var p in products) p?.Dispose();
                baseImg?.Dispose();
                baker.Dispose();
            }
        }

        /// <summary>
        /// 黑字的墨迹包围盒。底图是均匀灰 100，所以阈值 0..60 只圈字。
        /// 区域为空返回 null = 一个字都没画出来，调用方把这条判红并带上四组具体数字。
        /// </summary>
        private static (int R1, int C1, int R2, int C2)? InkBox(HImage mono)
        {
            HOperatorSet.Threshold(mono, out HObject dark, 0, 60);
            try
            {
                HOperatorSet.AreaCenter(dark, out HTuple area, out _, out _);
                if (area.I <= 0) return null;
                HOperatorSet.SmallestRectangle1(dark, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
                return (r1.I, c1.I, r2.I, c2.I);
            }
            finally { dark.Dispose(); }
        }

        private static string Fmt((int R1, int C1, int R2, int C2)? b) =>
            b == null ? "无墨迹" : $"r{b.Value.R1}..{b.Value.R2} c{b.Value.C1}..{b.Value.C2}";

        // ==================================================================
        //  画布与采样辅助
        // ==================================================================

        private static BakePlan LineAt(double row)
        {
            var p = new BakePlan();
            p.Shapes.Add(new BakeShape(BakeShapeKind.Line, new double[] { row, 40, row, 160 }, "white", 2));
            return p;
        }

        /// <summary>均匀灰度 byte 画布（GenImageConst + GenImageProto 是复制语义，中间对象即刻释放）</summary>
        private static HImage GrayCanvas(int w, int h, int gray)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", w, h);
            HOperatorSet.GenImageProto(proto, out HObject body, gray);
            proto.Dispose();
            var img = new HImage(body);
            body.Dispose();
            return img;
        }

        /// <summary>在图上涂一块指定灰度的矩形（paint_region 的 Type 只认 "fill"，写 "filled" 抛 #1302）</summary>
        private static HImage PaintRect(HImage src, int r1, int c1, int r2, int c2, double gray)
        {
            HOperatorSet.GenRectangle1(out HObject rect, r1, c1, r2, c2);
            HOperatorSet.PaintRegion(rect, src, out HObject painted, gray, "fill");
            rect.Dispose();
            var img = new HImage(painted);
            painted.Dispose();
            return img;
        }

        /// <summary>造一张 4 通道 byte 图（RGBA）：逐通道生成后 AppendChannel 三次拼起来</summary>
        private static HImage Build4Channel(int w, int h, List<HObject> temps)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", w, h);
            temps.Add(proto);

            HOperatorSet.GenImageProto(proto, out HObject c1, 100);
            HOperatorSet.GenImageProto(proto, out HObject c2, 120);
            HOperatorSet.GenImageProto(proto, out HObject c3, 140);
            HOperatorSet.GenImageProto(proto, out HObject c4, 200);
            temps.Add(c1); temps.Add(c2); temps.Add(c3); temps.Add(c4);

            HOperatorSet.AppendChannel(c1, c2, out HObject c12);
            temps.Add(c12);
            HOperatorSet.AppendChannel(c12, c3, out HObject c123);
            temps.Add(c123);
            HOperatorSet.AppendChannel(c123, c4, out HObject rgba);
            temps.Add(rgba);

            return new HImage(rgba);
        }

        /// <summary>取第 1 通道的单通道视图，便于用 GetGrayval 采样（烘焙产物可能是 3 通道）</summary>
        private static HImage ToMono(HImage img)
        {
            HOperatorSet.CountChannels(img, out HTuple ch);
            if (ch.I <= 1) return new HImage(img);
            HOperatorSet.AccessChannel(img, out HObject c1, 1);
            var mono = new HImage(c1);
            c1.Dispose();
            return mono;
        }

        private static double Sample(HImage mono, int row, int col)
        {
            HOperatorSet.GetGrayval(mono, row, col, out HTuple g);
            return g.D;
        }

        private static (double Min, double Max, double Range) StatsOf(HImage mono, int r1, int c1, int r2, int c2)
        {
            HOperatorSet.GenRectangle1(out HObject rect, r1, c1, r2, c2);
            try
            {
                HOperatorSet.MinMaxGray(rect, mono, 0, out HTuple min, out HTuple max, out HTuple range);
                return (min[0].D, max[0].D, range[0].D);
            }
            finally { rect.Dispose(); }
        }

        private static (int W, int H) SizeOf(HImage img)
        {
            HOperatorSet.GetImageSize(img, out HTuple w, out HTuple h);
            return (w.I, h.I);
        }

        /// <summary>灰度比对容差 ±6：烘焙经过"显示 → 回读"，有插值/抗锯齿抖动，不能要求逐位相等</summary>
        private static bool Near(double actual, double expect) => Math.Abs(actual - expect) <= 6;
    }
}
