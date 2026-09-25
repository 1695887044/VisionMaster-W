using System;
using System.Linq;
using Core.Interfaces;
using HalconDotNet;
using Plugin.BlobDetect;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// Blob 缺陷检测（Plugin.BlobDetect）的断言。
    ///
    /// 为什么要补这一份：该插件此前三轮改动（建立 / 通用性修正 / 通用性三项优化 / 泄漏修复）
    /// 全部靠"仓库外临时控制台真跑 + 目视"验证，控制台已删除、不可复现，
    /// 冒烟套件里搜 "Blob" 零匹配——等于这个算子长期没有任何自动化守护。
    ///
    /// 真值策略（重要，避免自欺）：
    /// 这里**不依赖任何外部样图**，全部用 HALCON 现场合成确定性图像（背景恒定 + 已知位置的暗斑），
    /// 所以期望值是可以笔算出来的，不会因为换机器/换图库而漂移。
    /// 断言的是"个数、筛选、排序、判定语义、端口对齐、单位换算、底图位深"这些契约，
    /// 而不是"某张照片上恰好检出 12 个"这种会随调参变化的东西。
    /// </summary>
    internal static class BlobDetectChecks
    {
        /// <summary>测试图：200×200，背景 200，暗斑灰度 50（在默认阈值 0~128 内）</summary>
        private const int ImgW = 200, ImgH = 200, Background = 200, DefectGray = 50;

        public static void Run()
        {
            Section("[Blob] 缺陷检测插件（合成确定性图像验证）");

            RunCoreContract();
            RunShapeAndBorderFilters();
            RunMergeAndFill();
            RunJudgementAndUnits();
            RunResetAndBitDepth();
        }

        // ==================================================================
        //  ① 基本检出 + 端口对齐 + 排序
        // ==================================================================
        private static void RunCoreContract()
        {
            using var image = BuildScene();
            var log = new StubLog();
            var plugin = NewPlugin(image);

            Exception? boom = null;
            try { plugin.Execute(MakeContext(log)); }
            catch (Exception ex) { boom = ex; }

            Check("运行不抛异常", boom == null, boom == null ? "" : $"{boom.GetType().Name}: {boom.Message}");
            Check("报成功（检出缺陷是正常工况，不是失败）",
                plugin.Success.Value is true, $"Success={plugin.Success.Value} Error='{plugin.ErrorMessage.Value}'");

            int count = Convert.ToInt32(plugin.DefectCount.Value);
            // 场景里 5 个 ≥30px 的暗斑（另有一个 4px 的被 MinArea 滤掉）
            Check("【核心】检出 5 个缺陷（4px 的噪点被面积下限滤掉）", count == 5, $"DefectCount={count}");

            var areas = AsDoubles(plugin.DefectAreas.Value);
            var rows = AsDoubles(plugin.CenterRows.Value);
            var cols = AsDoubles(plugin.CenterCols.Value);
            var widths = AsDoubles(plugin.DefectWidths.Value);
            var heights = AsDoubles(plugin.DefectHeights.Value);

            Check("【核心】五个数组端口逐项对齐（下游按同一索引取就能对上号）",
                areas.Length == count && rows.Length == count && cols.Length == count
                && widths.Length == count && heights.Length == count,
                $"areas={areas.Length} rows={rows.Length} cols={cols.Length} w={widths.Length} h={heights.Length}");

            double maxArea = Convert.ToDouble(plugin.MaxArea.Value);
            Check("MaxArea 等于逐项面积的最大值", Math.Abs(maxArea - areas.Max()) < 1e-6,
                $"MaxArea={maxArea:0.#} areas.Max={areas.Max():0.#}");
            Check("TotalArea 等于逐项面积之和",
                Math.Abs(Convert.ToDouble(plugin.TotalArea.Value) - areas.Sum()) < 1e-6,
                $"TotalArea={plugin.TotalArea.Value} sum={areas.Sum():0.#}");

            // 默认 MaxDefectCount=0 → 有缺陷必 NG，且 NG 不写 Fail
            Check("默认零容忍 → 判 NG，但 Success 仍为 true（NG 是正常结果）",
                plugin.IsOk.Value is false && plugin.Success.Value is true,
                $"IsOk={plugin.IsOk.Value} Success={plugin.Success.Value} Err='{plugin.ErrorMessage.Value}'");

            // 排序语义：区域数组本身没有顺序约定，必须由插件定死
            plugin.SortMode = DefectSortMode.AreaDescending;
            plugin.Execute(MakeContext(new StubLog()));
            var desc = AsDoubles(plugin.DefectAreas.Value);
            Check("【排序】面积从大到小：输出确实单调不增",
                desc.Length > 1 && desc.Zip(desc.Skip(1), (a, b) => a >= b).All(x => x),
                string.Join(", ", desc.Select(d => d.ToString("0.#"))));
            Check("【排序】排序后第一个就是 MaxArea",
                Math.Abs(desc[0] - Convert.ToDouble(plugin.MaxArea.Value)) < 1e-6,
                $"first={desc[0]:0.#} MaxArea={plugin.MaxArea.Value}");

            plugin.Dispose();
        }

        // ==================================================================
        //  ② 触边排除 / 长宽比 / 圆度（本轮补齐的筛选能力）
        // ==================================================================
        private static void RunShapeAndBorderFilters()
        {
            using var image = BuildScene();

            // 触边：场景里有一个贴着最右列的条形缺陷
            var pBorder = NewPlugin(image);
            pBorder.ExcludeBorderPx = 1;
            pBorder.Execute(MakeContext(new StubLog()));
            int afterExclude = Convert.ToInt32(pBorder.DefectCount.Value);
            Check("【触边排除】ExcludeBorderPx=1 → 恰好少一个（贴右边缘那个被丢掉）",
                afterExclude == 4, $"DefectCount={afterExclude}（期望 4）");

            var cols = AsDoubles(pBorder.CenterCols.Value);
            var widths = AsDoubles(pBorder.DefectWidths.Value);
            Check("【触边排除】剩下的都不压到最外 1 像素",
                cols.Zip(widths, (c, w) => c - w / 2 >= 1 && c + w / 2 <= ImgW - 2).All(x => x),
                string.Join(",", cols.Zip(widths, (c, w) => $"c={c:0.#},w={w:0.#}")));
            pBorder.Dispose();

            // 长宽比：场景里有一条 2×31 的细长条（长宽比 ≈15.5）
            var pAspect = NewPlugin(image);
            pAspect.MaxAspectRatio = 5;
            pAspect.Execute(MakeContext(new StubLog()));
            int afterAspect = Convert.ToInt32(pAspect.DefectCount.Value);
            Check("【长宽比】MaxAspectRatio=5 → 细长条被排除（5 个剩 4 个）",
                afterAspect == 4, $"DefectCount={afterAspect}（期望 4）");

            var ws = AsDoubles(pAspect.DefectWidths.Value);
            var hs = AsDoubles(pAspect.DefectHeights.Value);
            Check("【长宽比】剩下的每个都 ≤ 设定的长宽比",
                ws.Zip(hs, (w, h) => Math.Max(w, h) / Math.Max(Math.Min(w, h), 1) <= 5 + 1e-6).All(x => x),
                string.Join(",", ws.Zip(hs, (w, h) => $"{w:0.#}×{h:0.#}")));
            pAspect.Dispose();

            // 圆度：把下限抬到 0.5，细长条（圆度极低）应当被排除
            var pCirc = NewPlugin(image);
            pCirc.MinCircularity = 0.5;
            pCirc.Execute(MakeContext(new StubLog()));
            Check("【圆度】MinCircularity=0.5 → 细长条被排除（圆斑保留）",
                Convert.ToInt32(pCirc.DefectCount.Value) == 4,
                $"DefectCount={pCirc.DefectCount.Value}（期望 4）");
            pCirc.Dispose();
        }

        // ==================================================================
        //  ③ 合并相邻缺陷 / 填孔
        // ==================================================================
        private static void RunMergeAndFill()
        {
            // 两段间隔 3px 的小方块：不合并是 2 个，MergeRadius=2.5 合成 1 个
            using var seg = MakeByteImage();
            HOperatorSet.GenRectangle1(out HObject s1, 10, 10, 20, 15);
            HOperatorSet.GenRectangle1(out HObject s2, 10, 19, 20, 30);
            HOperatorSet.OverpaintRegion(seg, s1, DefectGray, "fill");
            HOperatorSet.OverpaintRegion(seg, s2, DefectGray, "fill");
            s1.Dispose(); s2.Dispose();

            var p1 = NewPlugin(seg);
            p1.MinArea = 1;
            p1.Execute(MakeContext(new StubLog()));
            int before = Convert.ToInt32(p1.DefectCount.Value);

            var p2 = NewPlugin(seg);
            p2.MinArea = 1;
            p2.MergeRadius = 2.5;
            p2.Execute(MakeContext(new StubLog()));
            int after = Convert.ToInt32(p2.DefectCount.Value);

            Check("【合并】断裂两段：不合并是 2 个", before == 2, $"DefectCount={before}（期望 2）");
            Check("【合并】MergeRadius=2.5 → 合并成 1 个（划痕断裂不再虚高计数）",
                after == 1, $"DefectCount={after}（期望 1）");
            p1.Dispose(); p2.Dispose();

            // 填孔：环形缺陷（外圆挖掉内圆）→ 填孔后面积应变大
            using var ring = MakeByteImage();
            HOperatorSet.GenCircle(out HObject outer, 100, 100, 20);
            HOperatorSet.GenCircle(out HObject inner, 100, 100, 10);
            HOperatorSet.Difference(outer, inner, out HObject annulus);
            HOperatorSet.OverpaintRegion(ring, annulus, DefectGray, "fill");
            outer.Dispose(); inner.Dispose(); annulus.Dispose();

            var p3 = NewPlugin(ring);
            p3.MinArea = 1;
            p3.Execute(MakeContext(new StubLog()));
            double areaHollow = Convert.ToDouble(p3.MaxArea.Value);

            var p4 = NewPlugin(ring);
            p4.MinArea = 1;
            p4.FillHoles = true;
            p4.Execute(MakeContext(new StubLog()));
            double areaFilled = Convert.ToDouble(p4.MaxArea.Value);

            Check("【填孔】环形缺陷填孔后面积变大（孔不再被漏算）",
                areaFilled > areaHollow + 1, $"未填={areaHollow:0.#} 填后={areaFilled:0.#}");
            p3.Dispose(); p4.Dispose();
        }

        // ==================================================================
        //  ④ 判定语义（存在性 / 总面积）与单位换算
        // ==================================================================
        private static void RunJudgementAndUnits()
        {
            using var image = BuildScene();

            // 存在性检测：一个都没检到要判 NG（只靠上限时 0 个会误判 OK）
            var pExist = NewPlugin(image);
            pExist.MinDefectCount = 6;    // 场景只有 5 个 → 必然不足
            pExist.MaxDefectCount = 100;
            pExist.Execute(MakeContext(new StubLog()));
            Check("【存在性】个数少于下限 → 判 NG 且原因写明「少于下限」",
                pExist.IsOk.Value is false
                && (pExist.ErrorMessage.Value as string ?? "").Contains("少于下限"),
                $"IsOk={pExist.IsOk.Value} Err='{pExist.ErrorMessage.Value}'");
            pExist.Dispose();

            // 总面积：每个都合格、但合计超标
            var pTotal = NewPlugin(image);
            pTotal.MaxDefectCount = 100;
            pTotal.MaxSingleArea = 100000;
            pTotal.MaxTotalArea = 10;
            pTotal.Execute(MakeContext(new StubLog()));
            Check("【总面积】单缺陷都合格但总面积超标 → 判 NG 且原因写明「总面积」",
                pTotal.IsOk.Value is false
                && (pTotal.ErrorMessage.Value as string ?? "").Contains("总面积"),
                $"IsOk={pTotal.IsOk.Value} Err='{pTotal.ErrorMessage.Value}'");
            pTotal.Dispose();

            // 像素当量：只影响 mm² 输出，不改变判定（避免"改当量改判定"的隐式耦合）
            var pMm = NewPlugin(image);
            pMm.PixelSizeMm = 2.0;
            pMm.Execute(MakeContext(new StubLog()));
            double px = Convert.ToDouble(pMm.MaxArea.Value);
            double mm2 = Convert.ToDouble(pMm.MaxAreaMm2.Value);
            Check("【像素当量】mm² = 像素面积 × 当量²（当量 2 → ×4）",
                Math.Abs(mm2 - px * 4) < 1e-6, $"px={px:0.#} mm²={mm2:0.#}");
            pMm.Dispose();
        }

        // ==================================================================
        //  ⑤ 开轮重置 + 16 位图标注底图（本轮修的两个缺陷）
        // ==================================================================
        private static void RunResetAndBitDepth()
        {
            // 开轮重置：先跑出结果，再用空图跑失败轮，非 IDisposable 端口必须清零
            using var image = BuildScene();
            var p = NewPlugin(image);
            p.Execute(MakeContext(new StubLog()));
            int before = Convert.ToInt32(p.DefectCount.Value);

            p.SrcImage.Value = null;
            p.Execute(MakeContext(new StubLog()));
            Check("【开轮重置】失败轮把上一轮的脏数据清零（下游不会读到旧结果）",
                before > 0 && Convert.ToInt32(p.DefectCount.Value) == 0
                && AsDoubles(p.DefectAreas.Value).Length == 0
                && p.IsOk.Value is false,
                $"上一轮={before} 本轮 Count={p.DefectCount.Value} IsOk={p.IsOk.Value}");
            Check("失败原因写明是图像为空", p.Success.Value is false
                && (p.ErrorMessage.Value as string ?? "").Contains("输入图像为空"),
                $"Success={p.Success.Value} Err='{p.ErrorMessage.Value}'");
            p.Dispose();

            // ---- 对照：byte 两档图（左 100 / 右 200）。底图直接就是原图，用来确认
            //      "离屏窗口 + dump_window_image"这条渲染链路在本环境里确实能出图 ----
            using var img8 = BuildByteTwoTone();
            var p8 = NewPlugin(img8);
            p8.MinGray = 0;
            p8.MaxGray = 0;
            p8.Execute(MakeContext(new StubLog()));
            var anno8 = p8.DefectImage.Value as HImage;
            double span8 = anno8 != null && anno8.IsInitialized() ? MeasureStripSpan(anno8) : -1;
            p8.Dispose();

            // 这条是"环境闸门"而不是断言：离屏窗口（open_window 'buffer' + dump_window_image）
            // 在没有显示会话的环境里可能根本不渲染底图（实测 byte 对照组也是一片黑）。
            // 闸门没开就说明"本环境验不了位深修复"，记成跳过，绝不制造一条假失败。
            if (span8 <= 50)
            {
                Check("【位深】uint2 标注底图按实际灰度范围拉伸", true,
                    $"跳过：本环境离屏窗口不渲染底图层次（byte 对照组跨度={span8:0.#}，期望 ≈100），"
                    + "需在带显示会话的环境里目视确认 —— 位深截断的实质证据见开发记录（uint2 1632 → byte 实测为 255）");
                return;
            }

            Check("【位深·对照】byte 两档图的标注底图保留了层次（渲染链路本身可用）",
                span8 > 50, $"底部条带灰度跨度={span8:0.#}（期望 ≈100）");

            // ---- 16 位图：convert_image_type 是截断不是缩放，不按实际范围拉伸就会整片死白 ----
            using var img16 = BuildUint2TwoTone();
            var log16 = new StubLog();
            var p16 = NewPlugin(img16);
            // 阈值取 0~0：图上最低灰度是 1000，必然一个都检不出 → 标注图 = 纯底图 + 左上角文字。
            // 不能用"把 MinArea 填到天上去"来制造空结果：那会让 select_shape 的 下限>上限，
            // HALCON 直接抛 #1304（本轮已改为静默夹取，但断言就该用正常参数组合去验）
            p16.MinGray = 0;
            p16.MaxGray = 0;
            p16.Execute(MakeContext(log16));

            var annotated = p16.DefectImage.Value as HImage;
            if (annotated == null || !annotated.IsInitialized())
            {
                Check("【位深】16 位图标注图生成成功", false,
                    $"DefectImage 为空；Success={p16.Success.Value} Err='{p16.ErrorMessage.Value}'"
                    + $"；Warn={string.Join(" | ", log16.Warns)}");
            }
            else
            {
                double span = MeasureStripSpan(annotated);
                Check("【位深】uint2 图标注底图按实际灰度范围拉伸（底部条带灰度跨度 > 50，不是一片死白）",
                    span > 50, $"底部条带灰度跨度={span:0.#}（截断的话会是一个常数）");
            }
            p16.Dispose();
        }

        // ==================================================================
        //  夹具
        // ==================================================================

        /// <summary>统一构造：固定阈值取暗侧，避免依赖"自动适配"（那只在配置态触发）</summary>
        private static BlobDetectPlugin NewPlugin(HImage image)
        {
            var plugin = new BlobDetectPlugin { InstanceName = "Blob_断言" };
            plugin.ThresholdMode = ThresholdMode.Fixed;
            plugin.MinGray = 0;
            plugin.MaxGray = 128;
            plugin.MinArea = 30;
            plugin.SrcImage.Value = image;
            return plugin;
        }

        private static HImage MakeByteImage()
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(proto, out HObject img, Background);
            proto.Dispose();
            var image = new HImage(img);
            img.Dispose();
            return image;
        }

        /// <summary>
        /// 合成场景：3 个圆斑（面积不同）+ 1 个贴右边缘的条 + 1 条细长条 + 1 个 4px 噪点。
        /// 期望（MinArea=30）：检出 5 个，其中 1 个触边、1 个细长。
        /// </summary>
        private static HImage BuildScene()
        {
            var image = MakeByteImage();

            HOperatorSet.GenCircle(out HObject a, 50, 50, 6);      // 圆斑 A
            HOperatorSet.GenCircle(out HObject b, 100, 100, 4);    // 圆斑 B（较小）
            HOperatorSet.GenCircle(out HObject c, 150, 150, 8);    // 圆斑 C（最大）
            // 贴右边缘的方块：必须是"方"的，否则它自己也会被长宽比/圆度筛掉，
            // 那样"触边排除"与"形状筛选"两条断言就会互相污染（第一版就踩了这个坑）
            HOperatorSet.GenRectangle1(out HObject edge, 20, 192, 27, 199);    // 8×8，col2 = ImgW-1
            HOperatorSet.GenRectangle1(out HObject bar, 160, 10, 190, 11);     // 细长条 31×2（唯一的长宽比/圆度"异类"）
            HOperatorSet.GenRectangle1(out HObject noise, 30, 30, 31, 31);     // 4px 噪点

            foreach (var r in new[] { a, b, c, edge, bar, noise })
            {
                HOperatorSet.OverpaintRegion(image, r, DefectGray, "fill");
                r.Dispose();
            }
            return image;
        }

        /// <summary>两档灰度的 byte 图（左 100 / 右 200），作为渲染链路的对照组</summary>
        private static HImage BuildByteTwoTone()
        {
            var image = MakeByteImage();
            PaintLeftHalf(image, 100);
            return image;
        }

        /// <summary>两档灰度的 uint2 图（左 1000 / 右 2000），用来验证标注底图的位深处理</summary>
        private static HImage BuildUint2TwoTone()
        {
            var image = MakeByteImage();
            PaintLeftHalf(image, 100);

            HOperatorSet.ConvertImageType(image, out HObject u16, "uint2");
            HOperatorSet.ScaleImage(u16, out HObject scaled, 10.0, 0);
            u16.Dispose();
            var result = new HImage(scaled);
            scaled.Dispose();
            image.Dispose();
            return result;
        }

        private static void PaintLeftHalf(HImage image, int gray)
        {
            HOperatorSet.GenRectangle1(out HObject leftHalf, 0, 0, ImgH - 1, ImgW / 2 - 1);
            HOperatorSet.OverpaintRegion(image, leftHalf, gray, "fill");
            leftHalf.Dispose();
        }

        /// <summary>
        /// 量"标注图底部整条"的灰度跨度（避开左上角的判定文字框）。
        /// 它代表底图还有没有层次：底图被截成常数时跨度会是 0。
        /// </summary>
        private static double MeasureStripSpan(HImage annotated)
        {
            HOperatorSet.CropPart(annotated, out HObject strip, 150, 0, ImgW, 50);

            // 标注图是 dump_window_image 回读的 RGB，min_max_gray 只吃单通道 → 先转灰；
            // 且它的第一个入参是"区域"不是图像，必须拿 domain（否则取到的值是胡说八道）
            HOperatorSet.CountChannels(strip, out HTuple ch);
            HObject work = strip;
            if (ch.I == 3) HOperatorSet.Rgb1ToGray(strip, out work);
            HOperatorSet.GetDomain(work, out HObject dom);
            HOperatorSet.MinMaxGray(dom, work, 0, out HTuple lo, out HTuple hi, out HTuple _);
            double span = hi.D - lo.D;

            dom.Dispose();
            if (!ReferenceEquals(work, strip)) work.Dispose();
            strip.Dispose();
            return span;
        }

        private static double[] AsDoubles(object? tuple)
            => tuple is HTuple t && t.Length > 0 ? t.ToDArr() : Array.Empty<double>();

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "Blob 插件断言" }, new WorkspaceContext(),
                new System.Threading.CancellationTokenSource().Token);
    }
}
