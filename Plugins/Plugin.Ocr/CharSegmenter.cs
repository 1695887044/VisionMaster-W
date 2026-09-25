using System;
using System.Collections.Generic;
using HalconDotNet;

namespace Plugin.Ocr
{
    /// <summary>
    /// 字符分割器：把「识别区里的一行字符」切成一个个字符区域，并按阅读顺序排好。
    ///
    /// 为什么这一层是这个插件的主干
    /// ---------
    /// MLP 分类器（<c>do_ocr_multi_class_mlp</c>）只回答"这一小块像素像哪个字符"，
    /// 它**不负责把字切开**。阈值切歪一点、膨胀多一轮，字符就被粘成一个大块或碎成一堆点，
    /// 而分类器不会报错 —— 它只会给出一串看似正常的错字符。这类失败最阴，
    /// 所以调参旋钮全部集中在这一层。
    ///
    /// 流程（与 Plugin.ImageScript 里已验证的 OCR 脚本原型同构）
    /// ---------
    ///   ROI 裁剪 → 灰度 → 极性二值化 → 可选腐蚀/膨胀 → 连通域 → 形状与面积筛选 → 阅读顺序排序
    ///
    /// 极性为什么不用 invert_image
    /// ---------
    /// <c>binary_threshold</c> 的 LightDark 参数本身就是极性：
    /// 'dark' = 取暗区（暗字亮底）、'light' = 取亮区（亮字暗底）。
    /// 走算子参数而非先整幅反相，少一次全图运算，且"当前按哪种极性在切"在代码里一眼可见。
    ///
    /// 点阵字（针打 / 喷码）的要点
    /// ---------
    /// 字符由小圆点拼成，直接取连通域会得到几百个"点"而不是几个"字"。
    /// 要先 <c>dilation_circle</c> 把点焊成笔画再连通（脚本原型注释：半径从 1.5 起试，
    /// 过头会把相邻字符粘在一起）—— 这就是 <see cref="Options.DilationRadius"/> 的由来。
    /// 反过来，印刷字粘连时要用 <see cref="Options.ErosionRadius"/> 先腐蚀断开。
    ///
    /// 资源纪律
    /// ---------
    /// 每个 Halcon 中间对象都进 <c>owned</c> 列表、在 finally 里统一释放；
    /// 只有最终排序结果的所有权移交出去（由 <see cref="SegmentedChars"/> 负责释放）。
    /// Halcon 句柄是进程级非托管资源，漏一个就是一次缓慢泄漏。
    /// </summary>
    public sealed class CharSegmenter
    {
        /// <summary>分割参数。默认值按"标准印刷字符"给，点阵字只需把 DilationRadius 调到 1.5 左右</summary>
        public sealed class Options
        {
            /// <summary>暗字亮底（true，工业最常见的打码方式）还是亮字暗底（false）</summary>
            public bool TextIsDark { get; set; } = true;

            /// <summary>
            /// 用固定阈值（true）还是自动阈值（false）。
            /// 自动用 <c>binary_threshold</c> 的 max_separability（按直方图双峰找分界），
            /// 对均匀光照的图基本不用调；光照不匀时再切固定阈值。
            /// </summary>
            public bool UseFixedThreshold { get; set; }

            /// <summary>固定阈值：暗字时取灰度 &lt;= 本值的像素，亮字时取 &gt;= 本值</summary>
            public int FixedThreshold { get; set; } = 128;

            public int MinCharHeight { get; set; } = 8;
            public int MaxCharHeight { get; set; } = 300;
            public int MinCharWidth { get; set; } = 4;
            public int MaxCharWidth { get; set; } = 300;
            public int MinArea { get; set; } = 20;
            public int MaxArea { get; set; } = 5000;

            /// <summary>
            /// 闭运算半径（像素）：**点阵字焊合的首选**。0 = 不做。
            ///
            /// 为什么点阵字该用闭运算而不是纯膨胀：两者都能把散点连成笔画，但
            ///   膨胀 = 只扩不缩 → 笔画整体变粗、字形失真。MLP 分类器拿到一个"胖"字，
            ///   会把 0 认成 A、3 认成 T 这种系统性错（实测踩到过）；
            ///   闭运算 = 先扩后缩 → 点之间连上了，外轮廓尺寸基本还原。
            /// 所以"连接"用闭运算，"加粗"才用膨胀，两者不能互相替代。
            /// </summary>
            public double ClosingRadius { get; set; }

            /// <summary>腐蚀半径（像素）。印刷字粘连时先腐蚀断开；0 = 不腐蚀</summary>
            public double ErosionRadius { get; set; }

            /// <summary>
            /// 膨胀半径（像素）：把笔画整体加粗（细笔画印刷字用）。
            /// 注意它同时会外扩字形 —— 点阵字要的是"连接"而非"加粗"，请优先用 ClosingRadius。
            /// </summary>
            public double DilationRadius { get; set; }

            /// <summary>true = 按列优先排序（竖排文本），false = 按行优先（横排，默认）</summary>
            public bool ColumnFirst { get; set; }
        }

        /// <summary>
        /// 分割结果。持有"排序后的字符区域"与"识别要用的灰度图"，用完必须 Dispose。
        ///
        /// 为什么连灰度图一起带出来：分类器要的是「字符区域 + 该区域的灰度图」这一对，
        /// 若只给字符区域、让识别阶段再按同样的 ROI 裁一次+灰度化一次，
        /// 同一幅图的重复运算就白做了一遍（生产上这是每帧都要付的成本）。
        /// </summary>
        public sealed class SegmentResult : IDisposable
        {
            private HObject? _chars;
            private HObject? _grayImage;

            internal SegmentResult(
                HObject chars,
                HObject grayImage,
                double[] rows,
                double[] cols,
                double usedThreshold
            )
            {
                _chars = chars;
                _grayImage = grayImage;
                Rows = rows;
                Cols = cols;
                UsedThreshold = usedThreshold;
                Count = rows.Length;
            }

            /// <summary>排好序的字符区域（可直接喂给 do_ocr_multi_class_mlp）</summary>
            public HObject Chars => _chars
                ?? throw new ObjectDisposedException(nameof(SegmentResult));

            /// <summary>识别区内的灰度图（单通道），就是字符区域所在的那幅图</summary>
            public HObject GrayImage => _grayImage
                ?? throw new ObjectDisposedException(nameof(SegmentResult));

            /// <summary>每个字符的中心行（与阅读顺序对应，供投射与排障用）</summary>
            public double[] Rows { get; }

            /// <summary>每个字符的中心列</summary>
            public double[] Cols { get; }

            /// <summary>自动阈值实际取到的分界灰度（固定阈值时即设定值），试算界面会显示它</summary>
            public double UsedThreshold { get; }

            public int Count { get; }

            public void Dispose()
            {
                _chars?.Dispose();
                _chars = null;
                _grayImage?.Dispose();
                _grayImage = null;
            }
        }

        /// <summary>
        /// 切分并排序。失败时 <paramref name="error"/> 写清"怎么补"，不抛异常。
        /// </summary>
        public static bool TrySegment(
            HImage? image,
            double[]? roiParams,
            Options options,
            out SegmentResult? result,
            out string error
        )
        {
            result = null;
            error = string.Empty;

            if (image == null || !image.IsInitialized())
            {
                error = "输入图像为空";
                return false;
            }

            if (roiParams == null || roiParams.Length < 5)
            {
                error = "尚未框选识别区：请在节点配置里于图像上右键 → 新建矩形，框住整行字符";
                return false;
            }

            double centerRow = roiParams[0];
            double centerCol = roiParams[1];
            double phi = roiParams[2];
            double half1 = roiParams[3];
            double half2 = roiParams[4];

            if (half1 <= 0 || half2 <= 0)
            {
                error = "识别区的半长/半宽必须大于 0：请重新画一个框";
                return false;
            }

            // 所有中间对象都进这里，finally 统一释放 —— 漏一个就是一次非托管泄漏
            var owned = new List<HObject>();
            HObject? sorted = null;

            try
            {
                HOperatorSet.GenRectangle2(out HObject roi, centerRow, centerCol, phi, half1, half2);
                owned.Add(roi);

                HOperatorSet.ReduceDomain(image, roi, out HObject reduced);
                owned.Add(reduced);

                // 灰度：工业 OCR 多为单通道，但彩色图也允许直接连进来
                HObject graySource = reduced;
                HOperatorSet.CountChannels(reduced, out HTuple channels);
                if (channels.I > 1)
                {
                    HOperatorSet.Rgb1ToGray(reduced, out HObject gray);
                    owned.Add(gray);
                    graySource = gray;
                }

                // ---- 极性二值化 ----
                HObject binary;
                double usedThreshold;

                if (options.UseFixedThreshold)
                {
                    // 固定阈值：语义写死为"暗字取 <= T、亮字取 >= T"。
                    // 不借道 binary_threshold 的 'fixed' 模式 —— 那个模式的取值语义与 threshold 不同
                    // （它按直方图在给定值附近自适应），混用会让"设了 128 却切在别处"这种问题极难查。
                    if (options.TextIsDark)
                        HOperatorSet.Threshold(graySource, out binary, 0, options.FixedThreshold);
                    else
                        HOperatorSet.Threshold(graySource, out binary, options.FixedThreshold, 255);

                    usedThreshold = options.FixedThreshold;
                }
                else
                {
                    HOperatorSet.BinaryThreshold(
                        graySource,
                        out binary,
                        "max_separability",
                        options.TextIsDark ? "dark" : "light",
                        out HTuple usedThresholdTuple
                    );
                    usedThreshold = usedThresholdTuple.D;
                }

                owned.Add(binary);

                // ---- 可选形态学：闭运算焊合 → 腐蚀断开 → 膨胀加粗 ----
                HObject regionForConnection = binary;

                if (options.ClosingRadius > 0)
                {
                    HOperatorSet.ClosingCircle(regionForConnection, out HObject closed, options.ClosingRadius);
                    owned.Add(closed);
                    regionForConnection = closed;
                }

                if (options.ErosionRadius > 0)
                {
                    HOperatorSet.ErosionCircle(regionForConnection, out HObject eroded, options.ErosionRadius);
                    owned.Add(eroded);
                    regionForConnection = eroded;
                }

                if (options.DilationRadius > 0)
                {
                    HOperatorSet.DilationCircle(regionForConnection, out HObject dilated, options.DilationRadius);
                    owned.Add(dilated);
                    regionForConnection = dilated;
                }

                // ---- 连通域 + 三档筛选 ----
                // 三档为什么分开做、而不是 select_shape 多特征一次筛：
                //   ① 多特征要把特征名拼成元组，而 HTuple 的 operator+ 是**算术加**不是拼接，
                //      拼错的表现是 HALCON #3101「Unknown feature」—— 一个完全不指向真因的报错；
                //      单特征时特征名可以直接写字符串字面量，没有这层构造风险。
                //   ② 分开做能在切不出字符时报出"是哪一档筛光的"——现场据此就能知道该放宽哪个参数，
                //      而不是拿到一句"没切出字符"再从头试。
                HOperatorSet.Connection(regionForConnection, out HObject connected);
                owned.Add(connected);
                HOperatorSet.CountObj(connected, out HTuple rawCount);

                HOperatorSet.SelectShape(
                    connected, out HObject byHeight, "height", "and",
                    options.MinCharHeight, options.MaxCharHeight);
                owned.Add(byHeight);
                HOperatorSet.CountObj(byHeight, out HTuple heightCount);

                HOperatorSet.SelectShape(
                    byHeight, out HObject byWidth, "width", "and",
                    options.MinCharWidth, options.MaxCharWidth);
                owned.Add(byWidth);
                HOperatorSet.CountObj(byWidth, out HTuple widthCount);

                HOperatorSet.SelectShape(
                    byWidth, out HObject byArea, "area", "and",
                    options.MinArea, options.MaxArea);
                owned.Add(byArea);
                HOperatorSet.CountObj(byArea, out HTuple areaCount);

                if (areaCount.I == 0)
                {
                    error =
                        $"识别区里没有切出任何字符。筛选过程：连通域 {rawCount.I} 个"
                        + $" → 过高 {options.MinCharHeight}~{options.MaxCharHeight} 的有 {heightCount.I} 个"
                        + $" → 再过宽 {options.MinCharWidth}~{options.MaxCharWidth} 的有 {widthCount.I} 个"
                        + $" → 再过面积 {options.MinArea}~{options.MaxArea} 的有 0 个。"
                        + "请对着「在哪一档掉的」来放宽：第一档掉光通常是极性选错了或点阵没焊合（把膨胀半径调到 1.5 左右），"
                        + "第三档掉光通常是面积范围与字符实际大小不匹配";
                    return false;
                }

                // ---- 阅读顺序排序 ----
                HOperatorSet.SortRegion(
                    byArea,
                    out sorted,
                    "character",
                    "true",
                    options.ColumnFirst ? "column" : "row"
                );

                HOperatorSet.AreaCenter(sorted, out HTuple areas, out HTuple rows, out HTuple cols);

                var rowArray = new double[areaCount.I];
                var colArray = new double[areaCount.I];
                for (int i = 0; i < areaCount.I; i++)
                {
                    rowArray[i] = rows[i].D;
                    colArray[i] = cols[i].D;
                }

                result = new SegmentResult(sorted, graySource, rowArray, colArray, usedThreshold);

                // 所有权已移交 result，别在 finally 里释放掉；graySource 同理（它现在归 result 管）
                owned.Remove(graySource);
                sorted = null;
                return true;
            }
            catch (Exception ex)
            {
                // 越界 ROI 之类会让 Halcon 抛，这里转成可读原因；提示语必须写清"怎么补"
                error = $"字符分割失败：{ex.Message}（若为坐标越界，请把识别区画在图像范围内）";
                return false;
            }
            finally
            {
                foreach (var obj in owned)
                {
                    try { obj?.Dispose(); }
                    catch { /* 释放失败不打断整体回收 */ }
                }

                sorted?.Dispose();
            }
        }
    }
}
