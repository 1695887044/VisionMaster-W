using System;
using System.Collections.Generic;
using System.Linq;
using Core.Halcon.Color;

namespace Plugin.ColorCheck
{
    /// <summary>
    /// 采样剖面：沿采样区短轴逐点取到的三通道灰阶。
    /// 索引 0 是序列起点端（矩形坐标较小的那一端），索引增大即序列往后走。
    /// </summary>
    internal sealed class ColorProfile
    {
        internal ColorProfile(int[] r, int[] g, int[] b)
        {
            R = r;
            G = g;
            B = b;
        }

        internal int[] R { get; }
        internal int[] G { get; }
        internal int[] B { get; }

        internal int Length => R.Length;
    }

    /// <summary>
    /// 自适应找线的可调参数（对应界面上「高级参数」那几项）。
    ///
    /// 默认值面向「浅色背景 + 密排彩色线束 + 线间暗缝」这一类场景。
    /// 注意它们都是**无量纲的比例或百分位**，不是绝对灰阶值 ——
    /// 换灯、换工装改的是尺度，比例大体不变，这正是"自适应"的意义所在。
    /// </summary>
    internal sealed class SequenceSettings
    {
        /// <summary>手动指定线数；0 或负数 = 自动反推</summary>
        internal int ForceCount { get; set; }

        /// <summary>等间距规整化：开 = 按估出的线距铺满整束（能补出看不见的白线）；关 = 只报看得见的线</summary>
        internal bool Regularize { get; set; } = true;

        /// <summary>
        /// 暗线判据幅度（占"众数亮度到最暗像素"这段跨度的百分比）。
        /// 判定线 = 众数亮度 减去 这个百分比 × 跨度 —— 也就是"明显比线间缝还暗"才算可靠暗线。
        /// </summary>
        internal double DarkSpanRatio { get; set; } = 35;

        /// <summary>白线百分位：亮度高于这条线的算可靠线像素</summary>
        internal double BrightPercentile { get; set; } = 92;

        /// <summary>最小段长：连着这么多行都可靠才算一根线（用来滤噪点）</summary>
        internal int MinRun { get; set; } = 4;

        /// <summary>最大段长比例：长过剖面长度这个比例的可靠段按背景丢弃（防浅背景被当成线）</summary>
        internal double MaxRunRatio { get; set; } = 0.33;

        /// <summary>规整补色时的窗口半宽（线距的比例）</summary>
        internal double RuleWindowRatio { get; set; } = 0.30;

        /// <summary>端点外推：允许在检出范围两端各多探一根</summary>
        internal bool ExtendEnds { get; set; } = true;

        /// <summary>
        /// 颜色判据阈值：**由内核提供默认值，本算子带一份可覆盖的副本**
        /// （现场在「高级参数 → 颜色判据」里调，随方案落盘）。
        /// 所有颜色算子共用同一套词与同一套默认值，免得"玫红"在两个算子里给出两种答案。
        /// </summary>
        internal ColorThresholds Colors { get; set; } = new();
    }

    /// <summary>找线结果：颜色序列 + 每根的位置与来源 + 一路诊断量（诊断量给日志和调参用）</summary>
    internal sealed class SequenceResult
    {
        internal int Count { get; set; }
        internal string[] Names { get; set; } = Array.Empty<string>();

        /// <summary>每根线中心在剖面坐标上的位置（可能是小数）</summary>
        internal double[] Positions { get; set; } = Array.Empty<double>();

        /// <summary>每根线的来源：true = 来自检出的可靠段，false = 靠等间距规整补出来的</summary>
        internal bool[] FromSegment { get; set; } = Array.Empty<bool>();

        internal double Pitch { get; set; }
        internal double PitchFromSegments { get; set; }
        internal double PitchFromAutocorrelation { get; set; }
        internal double DarkThreshold { get; set; }
        internal double BrightThreshold { get; set; }
        internal double SaturationThreshold { get; set; }
        internal int SegmentCount { get; set; }

        /// <summary>端点外推补出来的根数（0~2）</summary>
        internal int ExtendedEnds { get; set; }

        /// <summary>
        /// 剖面索引到图像行号的换算表（由采样方填进来）。
        /// 分析器本身不碰坐标，只吐剖面坐标；要让"第 3 根在图像第几行"成立，
        /// 得靠这张表把剖面坐标翻译回图像坐标 —— 投射时要用。
        /// </summary>
        internal double[] RowsOfProfile { get; set; } = Array.Empty<double>();

        /// <summary>同上的列号换算表（投射时要把每根线的标签贴在它旁边）</summary>
        internal double[] ColsOfProfile { get; set; } = Array.Empty<double>();

        internal List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// 颜色序列的自适应分析：从一条采样剖面算出「几根、什么颜色、每根从哪来」。
    ///
    /// 为什么不扔开"先找可靠线"这条骨架
    /// ---------
    /// 白线（V≈240）与浅色背景（V≈235）只差几个灰阶，颜色上根本不可分。
    /// 任何"找边界"的思路（阈值分割、边缘、变化点检测）到了白线这里都会断。
    /// 所以骨架必须是：**抓得到的先抓（暗 / 彩 / 极亮），由它们的间距估出线距，
    /// 再把整条链按等间距铺开** —— 抓不到的那根就被补回来了。
    ///
    /// 那"自适应"改的是什么
    /// ---------
    /// 只改骨架里的三个**估计环节**，把脚本版的硬编码经验值换成从本幅图现算的量：
    ///   ① 判据阈值    通道极差走 Otsu、亮度走百分位（不再写死 150 / 40 / 240）
    ///   ② 线距        自相关在整条剖面上估主周期，与"相邻可靠段中心差的中位数"交叉验证
    ///   ③ 相位与端点  对可靠段中心做最小二乘回归，外推覆盖整束
    /// 第 ③ 条是关键：脚本版拿"第一个可靠段"当起点，端部那根一旦漏检就**整条序列错位一格**；
    /// 改成回归定锚后，端部漏检只会少一根，不会整体错位。
    ///
    /// 本类不碰 WPF、不碰 Halcon 句柄，输入是纯数组 —— 所以断言可以直接喂构造数据。
    /// </summary>
    internal static class ColorSequenceAnalyzer
    {
        /// <summary>自相关与分段线距算出来的周期允许差多少（相对），超过就认为两者打架</summary>
        private const double PitchCrossTolerance = 0.25;

        /// <summary>端点外推时，判定"那一格里有东西"用的局部起伏比例</summary>
        private const double EndpointStructureRatio = 0.20;

        /// <summary>可靠段中心与待定线位相差不超过线距的这个比例，就认为它们是同一根</summary>
        private const double MatchToleranceRatio = 0.45;

        /// <summary>
        /// 剖面亮度极差的下限（灰阶级）：低于它说明采样区里没有图像结构，
        /// 判据只会把噪声当线，直接报错比硬凑根数好
        /// </summary>
        private const int MinContrastLevels = 25;

        internal static bool Analyze(ColorProfile profile, SequenceSettings settings, out SequenceResult result, out string error)
        {
            result = new SequenceResult();
            error = string.Empty;

            int n = profile.Length;
            if (n < 8)
            {
                error = $"采样区太窄：沿线束方向只有 {n} 个采样点，至少要 8 个（把框画宽一点）";
                return false;
            }

            // ---------- 0. 逐点的亮度与彩度 ----------
            var mx = new int[n];       // 最亮通道 = 明度
            var chroma = new int[n];   // 通道极差 = 彩度
            for (int i = 0; i < n; i++)
            {
                int r = profile.R[i], g = profile.G[i], b = profile.B[i];
                mx[i] = Math.Max(r, Math.Max(g, b));
                chroma[i] = mx[i] - Math.Min(r, Math.Min(g, b));
            }

            // ---------- 1. 判据阈值：全部从本幅图现算 ----------
            //
            // 先过一道"这里到底有没有东西"的闸：整条剖面的亮度极差太小，说明采样区落在
            // 空白背景或一片糊掉的地方 —— 这时候任何判据都只是把噪声当线，不如直接报错。
            // 25 个灰阶是个"结构下限"：真实的线束与线间缝至少差这么多。
            int brightest = mx.Max();
            int darkest = mx.Min();
            if (brightest - darkest < MinContrastLevels)
            {
                error = $"采样区里几乎没有明暗变化（亮度极差只有 {brightest - darkest} 级）："
                      + "多半框到空白背景上了，请把采样区移到线束上";
                return false;
            }

            //
            // 暗线这条是全部自适应里最要紧的一条，口径是"明显比线间缝还暗"：
            //   缝/背景的水平取剖面的**众数** —— 剖面里最常见的那个亮度，就是缝或背景；
            //   最暗的线像素取 **2 号百分位**（不用最小值：一个坏点就能把尺度拉跑）；
            //   判定线放在两者之间往下 DarkSpanRatio 的位置。
            // 实测两张样本图的安全窗口是 100~160；这个口径给出 154 与 114，都落在窗口里。
            // 而脚本版那个写死的 150 只是碰巧落在这个窗口里 —— 换一盏灯就要重新试。
            //
            // 彩度（有没有颜色）用大津法，实测它对结果极不敏感（20~110 都给出同一个根数），
            // 所以交给算法自己求，人不用管。白线用亮度的高位百分位。
            int modalLevel = Mode(mx);
            int darkestLevel = Percentile(mx, 2);
            result.DarkThreshold = modalLevel - settings.DarkSpanRatio / 100.0 * (modalLevel - darkestLevel);
            result.BrightThreshold = Percentile(mx, settings.BrightPercentile);
            result.SaturationThreshold = Otsu(chroma);

            bool[] reliable = new bool[n];
            for (int i = 0; i < n; i++)
            {
                reliable[i] = mx[i] < result.DarkThreshold
                           || chroma[i] > result.SaturationThreshold
                           || mx[i] > result.BrightThreshold;
            }

            // ---------- 2. 分段：连续可靠够长才算一根 ----------
            int maxRun = settings.MaxRunRatio > 0 ? (int)Math.Round(n * settings.MaxRunRatio) : 0;
            var segments = FindSegments(reliable, settings.MinRun, maxRun, result.Warnings);
            result.SegmentCount = segments.Count;

            if (segments.Count < 2)
            {
                error = $"采样区里只找到 {segments.Count} 段可靠线，凑不出线距（确认采样区框住了整条线束；"
                      + "若线束颜色都很浅，把「暗线判据幅度」调小一点）";
                return false;
            }

            var centers = segments.Select(s => (s.Lo + s.Hi) / 2.0).ToList();
            double pitchFromSegments = MedianGap(centers);
            result.PitchFromSegments = pitchFromSegments;

            // ---------- 3. 线距：自相关与分段线距交叉验证 ----------
            //
            // 谁说了算，是按实测数据定的，不是按直觉：
            //   两者接近（差在 25% 以内）→ 取平均，互相消噪；
            //   分段线距正好是自相关的整数倍（1.6 倍以上）→ 信自相关 —— 那正是"漏检一半致使
            //     中位间距变成两倍"的典型情形，这才是自相关真正的用武之地；
            //   其余不一致 → 信分段线距。实测样本图上自相关会给出 16 而真值是 21.5
            //     （边缘信号容易被线宽变化和斜线干扰），这时候听它的反而错。
            double pitchFromAc = EstimatePitchByAutocorrelation(mx, chroma, pitchFromSegments);
            result.PitchFromAutocorrelation = pitchFromAc;

            double pitch;
            double ratio = pitchFromAc > 0 ? pitchFromSegments / pitchFromAc : 0;
            bool acLooksLikeFundamental = pitchFromAc > 0
                                          && ratio >= 1.6
                                          && Math.Abs(ratio - Math.Round(ratio)) <= 0.15;

            if (pitchFromAc > 0 && Math.Abs(pitchFromAc - pitchFromSegments) / pitchFromSegments <= PitchCrossTolerance)
            {
                pitch = (pitchFromAc + pitchFromSegments) / 2;
            }
            else if (acLooksLikeFundamental)
            {
                pitch = pitchFromAc;
                result.Warnings.Add($"分段线距 {pitchFromSegments:0.0} 正好是自相关线距 {pitchFromAc:0.0} 的 {ratio:0.#} 倍，"
                                  + "按自相关走（中间漏检的线被补回来）");
            }
            else
            {
                pitch = pitchFromSegments;
                if (pitchFromAc > 0)
                    result.Warnings.Add($"自相关给出的线距 {pitchFromAc:0.0} 与分段线距 {pitchFromSegments:0.0} 差得较多，"
                                      + "以分段线距为准（自相关在边缘信号上容易被线宽变化带偏）");
            }

            if (pitch < 2 || pitch > n / 2.0)
            {
                error = $"线距估计异常（{pitch:0.0} 像素，剖面长 {n}）：确认采样区的短边方向与线束走向垂直";
                return false;
            }

            result.Pitch = pitch;

            // 关掉规整化 = 只报看得见的线（调参时用来对比"到底补出来了几根"）
            if (!settings.Regularize)
            {
                FillFromSegments(profile, segments, result, settings);
                return true;
            }

            // ---------- 4. 相位与端点：最小二乘回归定锚 ----------
            if (!FitLineByIteration(centers, pitch, out double offset, out double slope))
            {
                error = "线距与各段中心对不上，无法定位每根线：确认采样区的短边方向与线束走向垂直";
                return false;
            }

            int kMin = int.MaxValue, kMax = int.MinValue;
            foreach (var c in centers)
            {
                int k = (int)Math.Round((c - offset) / slope);
                if (k < kMin) kMin = k;
                if (k > kMax) kMax = k;
            }

            // ---------- 5. 端点外推（可关） ----------
            if (settings.ExtendEnds)
            {
                int globalRange = mx.Max() - mx.Min();
                double half = Math.Max(1.5, slope * MatchToleranceRatio);
                if (HasLocalStructure(mx, offset + (kMin - 1) * slope, half, globalRange)) { kMin--; result.ExtendedEnds++; }
                if (HasLocalStructure(mx, offset + (kMax + 1) * slope, half, globalRange)) { kMax++; result.ExtendedEnds++; }
            }

            // ---------- 6. 手动指定线数 ----------
            int autoCount = kMax - kMin + 1;
            if (settings.ForceCount > 0 && settings.ForceCount != autoCount)
            {
                int middle = (kMin + kMax) / 2;
                kMin = middle - (settings.ForceCount - 1) / 2;
                kMax = kMin + settings.ForceCount - 1;
                result.Warnings.Add($"线数按手动指定的 {settings.ForceCount} 根展开（自动估出的是 {autoCount} 根），以检出范围的中点为基准");
            }

            int count = kMax - kMin + 1;
            var names = new string[count];
            var positions = new double[count];
            var fromSegment = new bool[count];

            for (int i = 0; i < count; i++)
            {
                double pos = offset + (kMin + i) * slope;
                positions[i] = pos;

                Segment? hit = FindSegmentAround(segments, pos, slope * MatchToleranceRatio);
                int lo, hi;
                bool trimmed;
                if (hit != null)
                {
                    lo = hit.Value.Lo;
                    hi = hit.Value.Hi;
                    trimmed = true;
                }
                else
                {
                    int half = Math.Max(1, (int)Math.Round(slope * settings.RuleWindowRatio));
                    lo = (int)Math.Round(pos) - half;
                    hi = (int)Math.Round(pos) + half;
                    trimmed = false;
                }

                var (r, g, b) = MedianRgb(profile, lo, hi, trimmed);
                names[i] = ColorVocabulary.Classify(r, g, b, settings.Colors);
                fromSegment[i] = hit != null;
            }

            result.Count = count;
            result.Names = names;
            result.Positions = positions;
            result.FromSegment = fromSegment;
            return true;
        }

        /// <summary>
        /// 关掉规整化时的路径：每一段可靠区间就是一根线，不铺开、不外推。
        /// 这条路径用来回答「到底有几根是直接看得见的」，是调参时的对照组。
        /// </summary>
        private static void FillFromSegments(ColorProfile profile, List<Segment> segments,
            SequenceResult result, SequenceSettings settings)
        {
            result.Count = segments.Count;
            result.Names = new string[segments.Count];
            result.Positions = new double[segments.Count];
            result.FromSegment = new bool[segments.Count];

            for (int i = 0; i < segments.Count; i++)
            {
                var (r, g, b) = MedianRgb(profile, segments[i].Lo, segments[i].Hi, true);
                result.Names[i] = ColorVocabulary.Classify(r, g, b, settings.Colors);
                result.Positions[i] = (segments[i].Lo + segments[i].Hi) / 2.0;
                result.FromSegment[i] = true;
            }
        }

        // ==================================================================
        //  分段
        // ==================================================================

        private readonly struct Segment
        {
            internal Segment(int lo, int hi) { Lo = lo; Hi = hi; }
            internal int Lo { get; }
            internal int Hi { get; }
        }

        private static List<Segment> FindSegments(bool[] reliable, int minRun, int maxRun, List<string> warnings)
        {
            var list = new List<Segment>();
            int n = reliable.Length;
            int dropped = 0;
            int start = -1;

            for (int i = 0; i <= n; i++)
            {
                bool on = i < n && reliable[i];
                if (on)
                {
                    if (start < 0) start = i;
                }
                else if (start >= 0)
                {
                    int len = i - start;
                    if (len >= minRun)
                    {
                        // 长过阈值的可靠区间按背景丢弃：一"根"线不可能占掉剖面的一大截，
                        // 而浅色背景被百分位判据误收进来时恰好就是又长又平的一大段
                        if (maxRun > 0 && len > maxRun) dropped++;
                        else list.Add(new Segment(start, i - 1));
                    }
                    start = -1;
                }
            }

            if (dropped > 0)
                warnings.Add($"丢弃了 {dropped} 段过长的可靠区间（疑似浅色背景被算成了线）：把「白线百分位」调高或把采样区收紧些");

            return list;
        }

        private static double MedianGap(List<double> centers)
        {
            if (centers.Count < 2) return 0;
            var gaps = new List<double>();
            for (int i = 1; i < centers.Count; i++) gaps.Add(centers[i] - centers[i - 1]);
            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private static Segment? FindSegmentAround(List<Segment> segments, double pos, double tolerance)
        {
            Segment? best = null;
            double bestDistance = double.MaxValue;
            foreach (var s in segments)
            {
                double d = Math.Abs((s.Lo + s.Hi) / 2.0 - pos);
                if (d <= tolerance && d < bestDistance) { best = s; bestDistance = d; }
            }
            return best;
        }

        // ==================================================================
        //  线距：自相关（与分段线距交叉验证）
        // ==================================================================

        /// <summary>
        /// 用自相关估主周期。
        ///
        /// 信号选的是**边缘强度**（亮度差 + 彩度差的绝对值）而不是亮度本身：
        /// 每根线的上下两条边界都会在这里冒一个尖，于是信号的周期正好等于线距；
        /// 而亮度本身会被"黑线 vs 白线"的颜色差异压住，周期反而看不出来。
        ///
        /// 找峰的办法是"先等它衰减过零，再取第一个显著峰"，不能直接取全局最大 ——
        /// 相邻像素本来就相似，零延迟附近的自相关天然最高，直接取最大会给出 3 像素这种荒唐线距
        /// （这一版就是照着这个坑改的）。过零之后的第一个峰才是主周期。
        ///
        /// 自相关还容易犯**倍频**的错：真周期 p 的整数倍也是峰。这里拿分段线距（hint）
        /// 当参照，在峰候选里挑最贴近它的那个。估不出来（剖面没有明显周期）时返回 0，
        /// 由调用方退回分段线距。
        /// </summary>
        private static double EstimatePitchByAutocorrelation(int[] mx, int[] chroma, double hint)
        {
            int n = mx.Length;
            int kMin = 4;
            int kMax = Math.Min(n / 2, n - 2);
            if (kMax <= kMin) return 0;

            var sig = new double[n];
            for (int i = 0; i < n - 1; i++)
                sig[i] = Math.Abs(mx[i + 1] - mx[i]) + Math.Abs(chroma[i + 1] - chroma[i]);
            sig[n - 1] = n >= 2 ? sig[n - 2] : 0;

            double mean = sig.Average();
            double energy = 0;
            for (int i = 0; i < n; i++)
            {
                sig[i] -= mean;
                energy += sig[i] * sig[i];
            }
            if (energy <= 1e-6) return 0;

            var r = new double[kMax + 2];
            double maxR = double.MinValue;
            for (int k = kMin; k <= kMax; k++)
            {
                double acc = 0;
                for (int i = 0; i + k < n; i++) acc += sig[i] * sig[i + k];
                r[k] = acc / (n - k);   // 按重叠点数归一，免得大 lag 天然吃亏
                if (r[k] > maxR) maxR = r[k];
            }
            if (maxR <= 0) return 0;

            int afterZero = kMin;
            while (afterZero <= kMax && r[afterZero] > 0) afterZero++;
            if (afterZero > kMax) return 0;   // 一路为正：没有明显周期

            int lag = 0;
            for (int j = afterZero; j <= kMax; j++)
            {
                bool isPeak = (j == afterZero || r[j] >= r[j - 1]) && (j == kMax || r[j] >= r[j + 1]);
                if (isPeak && r[j] >= 0.25 * maxR) { lag = j; break; }
            }
            if (lag == 0) return 0;

            var candidates = new List<double> { lag };
            foreach (double factor in new[] { 0.5, 2.0, 1.0 / 3.0 })
            {
                double scaled = lag * factor;
                int idx = (int)Math.Round(scaled);
                if (idx >= kMin && idx <= kMax) candidates.Add(scaled);
            }

            return hint > 1 ? candidates.OrderBy(c => Math.Abs(c - hint)).First() : lag;
        }

        // ==================================================================
        //  相位 / 端点
        // ==================================================================

        /// <summary>
        /// 迭代拟合：先按当前锚点给每段分配线号，再回归出新的锚点与线距。
        /// 迭代两三轮就稳定 —— 这一步是"端部漏检不再整体错位"的关键。
        /// </summary>
        private static bool FitLineByIteration(List<double> centers, double pitch, out double offset, out double slope)
        {
            offset = centers[0];
            slope = pitch;

            for (int iter = 0; iter < 3; iter++)
            {
                var ks = new int[centers.Count];
                for (int i = 0; i < centers.Count; i++)
                    ks[i] = (int)Math.Round((centers[i] - offset) / slope);

                if (!FitLine(centers, ks, out double a, out double b)) return iter > 0;
                offset = a;
                slope = b;
            }

            return slope > 0.5;
        }

        private static bool FitLine(List<double> ys, int[] ks, out double intercept, out double slope)
        {
            intercept = 0;
            slope = 0;
            int m = ys.Count;
            if (m < 2) return false;

            double mk = 0, my = 0;
            for (int i = 0; i < m; i++) { mk += ks[i]; my += ys[i]; }
            mk /= m;
            my /= m;

            double sxx = 0, sxy = 0;
            for (int i = 0; i < m; i++)
            {
                double dk = ks[i] - mk;
                sxx += dk * dk;
                sxy += dk * (ys[i] - my);
            }
            if (sxx <= 1e-9) return false;

            slope = sxy / sxx;
            intercept = my - slope * mk;
            return slope > 0.5;
        }

        /// <summary>
        /// 端点外推的判据：这一格附近**有没有起伏**。
        /// 一根线在剖面里必然带来"亮-暗"的局部起伏；而线束外的平坦背景没有。
        /// 用"局部起伏 ≥ 全局起伏的 20%"当证据 —— 不要求能认出颜色，只要求"那里有东西"。
        /// </summary>
        private static bool HasLocalStructure(int[] mx, double pos, double halfWindow, int globalRange)
        {
            if (globalRange <= 0) return false;

            int lo = (int)Math.Round(pos - halfWindow);
            int hi = (int)Math.Round(pos + halfWindow);
            if (lo < 0 || hi >= mx.Length || hi <= lo) return false;

            int mn = int.MaxValue, mxc = int.MinValue;
            for (int i = lo; i <= hi; i++)
            {
                if (mx[i] < mn) mn = mx[i];
                if (mx[i] > mxc) mxc = mx[i];
            }

            return (mxc - mn) >= EndpointStructureRatio * globalRange;
        }

        // ==================================================================
        //  取色与统计小工具
        // ==================================================================

        /// <summary>取一段的中位色。trimmed = 掐掉上下各 1/4，避开线两侧的暗缝</summary>
        private static (int R, int G, int B) MedianRgb(ColorProfile profile, int lo, int hi, bool trimmed)
        {
            lo = Math.Max(0, lo);
            hi = Math.Min(profile.Length - 1, hi);
            if (hi < lo) return (0, 0, 0);

            if (trimmed)
            {
                int len = hi - lo + 1;
                if (len >= 8)
                {
                    lo += len / 4;
                    hi -= len / 4;
                }
            }

            var rr = new List<int>();
            var gg = new List<int>();
            var bb = new List<int>();
            for (int i = lo; i <= hi; i++)
            {
                rr.Add(profile.R[i]);
                gg.Add(profile.G[i]);
                bb.Add(profile.B[i]);
            }
            if (rr.Count == 0) return (0, 0, 0);

            rr.Sort();
            gg.Sort();
            bb.Sort();
            int m = rr.Count / 2;
            return (rr[m], gg[m], bb[m]);
        }

        private static int Percentile(int[] values, double percentile)
        {
            if (values.Length == 0) return 0;
            var sorted = (int[])values.Clone();
            Array.Sort(sorted);
            int idx = (int)Math.Round(Math.Clamp(percentile, 0, 100) / 100.0 * (sorted.Length - 1));
            return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
        }

        /// <summary>众数：剖面里出现次数最多的那个亮度 —— 线间缝/背景就住在这一档</summary>
        private static int Mode(int[] values)
        {
            if (values.Length == 0) return 0;
            var hist = new int[256];
            foreach (var v in values) hist[Math.Clamp(v, 0, 255)]++;
            int best = 0, at = 0;
            for (int i = 0; i < 256; i++) if (hist[i] > best) { best = hist[i]; at = i; }
            return at;
        }

        /// <summary>大津法（最大类间方差）自动求阈：把直方图分成「两类离得最开」的那一刀</summary>
        private static int Otsu(int[] values)
        {
            if (values.Length == 0) return 0;

            var hist = new int[256];
            foreach (var v in values) hist[Math.Clamp(v, 0, 255)]++;

            int total = values.Length;
            double sum = 0;
            for (int i = 0; i < 256; i++) sum += (double)i * hist[i];

            int weightBack = 0;
            double sumBack = 0;
            double best = -1;
            int threshold = 128;

            for (int t = 0; t < 256; t++)
            {
                weightBack += hist[t];
                if (weightBack == 0) continue;
                int weightFore = total - weightBack;
                if (weightFore == 0) break;

                sumBack += (double)t * hist[t];
                double meanBack = sumBack / weightBack;
                double meanFore = (sum - sumBack) / weightFore;
                double between = (double)weightBack * weightFore * (meanBack - meanFore) * (meanBack - meanFore);
                if (between > best) { best = between; threshold = t; }
            }

            return threshold;
        }
    }
}
