using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HalconDotNet;
using VisionMaster.Helpers;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// A1 / A2 性能基准（**选择性运行**：<c>FlowCanvasChecks.exe --perf</c>，常规断言不跑它）。
    ///
    /// 为什么单独成段且默认不跑
    /// ---------
    /// 计时断言天然抖动，混进常规套件会让"红了一项"永远说不清是真回归还是机器状态。
    /// 这里只做三件**确定性**的事：① 两侧产出一致（尺寸/非空）；② 回归护栏（改造后不得慢于改造前）；
    /// ③ 把数字打印出来供人工比对与基线落档。断言故意留得很松——差值在 60%~98%，护栏只需防"被改回去"。
    ///
    /// 方法学
    /// ---------
    /// · "改造前/改造后"两套实现**同进程**交替测量，排除机器状态与 HALCON 预热差异；
    /// · 每组先预热（JIT + HALCON 内部缓冲），再进入计时循环；报告 min/中位/均值，**中位**为主参考；
    /// · A1"改造后"一侧用缓存委托直调插件里的**生产实现**（私有静态，反射解析一次）；
    ///   绝不能用 MethodInfo.Invoke——它每次 new object[] + 装箱 3 个 int（约 190 B/次），
    ///   会把"改造后"的托管分配带偏（实测踩过）；
    /// · 托管分配用 GC.GetAllocatedBytesForCurrentThread。注意 AllocHGlobal 是**非托管**分配，
    ///   不计入该指标——所以 A1 的托管分配两侧都≈0 是**预期**，它的收益在时间（少一趟全帧拷贝）；
    /// · **必须 Release 构建**（Debug 的 JIT 不做优化，数字失真）：<c>dotnet build -c Release</c>。
    ///
    /// 基线（2026-10-04，本机 Release）——重跑后与它对比即为回归检查：
    ///   A1 灰度 1920×1080: 0.834 → 0.070 ms（省 91.6%）；彩色: 3.157 → 1.221 ms（省 61.3%）
    ///   A2 灰度 1920×1080: 0.836 → 0.065 ms（省 92.2%），托管 1.98MB → 17.7KB（省 99.1%）
    ///   A2 彩色 1920×1080: 4.910 → 0.194 ms（省 96.0%），托管 11.87MB → 89.1KB（省 99.3%）
    ///   A2 彩色 2448×2048: 13.277 → 0.256 ms（省 98.1%），托管 28.69MB → 130.3KB（省 99.6%）
    /// </summary>
    internal static class PerfChecks
    {
        private const int WarmupRounds = 20;

        public static void Run()
        {
            Section("[PF] A1/A2 性能基准（--perf 选择性运行；Release 构建才有意义）");

            Console.WriteLine($"构建: {(IsDebugBuild ? "Debug —— 数字仅供参考，请用 -c Release 重跑" : "Release/优化")}");
            Console.WriteLine();

            BenchA1("A1 灰度 1920x1080 (2MB)", 1920, 1080, 1);
            BenchA1("A1 彩色 1920x1080x3 (6MB)", 1920, 1080, 3);
            BenchA2("A2 缩略图 灰度 1920x1080", 1920, 1080, false);
            BenchA2("A2 缩略图 彩色 1920x1080", 1920, 1080, true);
            BenchA2("A2 缩略图 彩色 2448x2048x3 (5MP)", 2448, 2048, true);
        }

        private static bool IsDebugBuild
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        // ==================================================================
        //  A1：原始像素 → HImage
        // ==================================================================

        private static void BenchA1(string title, int w, int h, int channels)
        {
            Section(title);

            int length = w * h * channels;
            var pixels = new byte[length];
            for (int i = 0; i < length; i++) pixels[i] = (byte)(i % 251);

            var newCall = ResolveToHImage();
            if (newCall == null)
            {
                Check(title + "：解析生产实现", false, "反射未命中，跳过本组");
                return;
            }

            const int rounds = 300;

            for (int i = 0; i < WarmupRounds; i++)
            {
                using var a = OldToHImage(pixels, w, h, channels);
                using var b = newCall(pixels, w, h, channels);
            }

            // 两侧各产出一张图，抽查像素一致（比的是同一种转换，不是两种）
            var pOld = OldToHImage(pixels, w, h, channels);
            var pNew = newCall(pixels, w, h, channels);
            bool same = ImagesEqual(pOld, pNew);
            pOld.Dispose();
            pNew.Dispose();
            Check("[PF-A1] 改造前/改造后产出逐点一致（同一种转换才有可比性）",
                same, same ? "" : "像素不一致，本组数字无效");

            var oldTimes = TimeLoop(rounds, () => OldToHImage(pixels, w, h, channels));
            var newTimes = TimeLoop(rounds, () => newCall(pixels, w, h, channels));

            Report("A1", oldTimes, newTimes, oldTimes.Length, newTimes.Length,
                guardPct: 20,   // 回归护栏：改造后中位数必须至少快 20%（基线差值 60%+，留足余量防抖动误报）
                allocOldPerCall: -1, allocNewPerCall: -1);
        }

        /// <summary>改造前的实现（原样复刻：AllocHGlobal + 全帧 Marshal.Copy + FreeHGlobal）</summary>
        private static HImage OldToHImage(byte[] pixelData, int width, int height, int channels)
        {
            var image = new HImage();
            try
            {
                var length = width * height * channels;
                var pointer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.Copy(pixelData, 0, pointer, length);

                    if (channels >= 3)
                    {
                        image.GenImageInterleaved(pointer, "bgr", width, height, -1,
                            "byte", width, height, 0, 0, -1, 0);
                    }
                    else
                    {
                        image.GenImage1("byte", width, height, pointer);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }

                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 解析插件里的**生产实现**（私有静态 ToHImage）。
        /// 用缓存委托而不是 MethodInfo.Invoke：见类注释"方法学"最后一条。
        /// </summary>
        private static Func<byte[], int, int, int, HImage>? ResolveToHImage()
        {
            try
            {
                var mi = typeof(Plugin.ImageAcquisition.ImageAcquisitionPlugin).GetMethod(
                    "ToHImage",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(byte[]), typeof(int), typeof(int), typeof(int) },
                    null);
                return mi?.CreateDelegate<Func<byte[], int, int, int, HImage>>();
            }
            catch
            {
                return null;
            }
        }

        // ==================================================================
        //  A2：缩略图
        // ==================================================================

        private static void BenchA2(string title, int w, int h, bool color)
        {
            Section(title);

            const int maxSize = 160;
            var img = MakeImage(w, h, color);
            try
            {
                const int rounds = 200;

                for (int i = 0; i < WarmupRounds; i++)
                {
                    var _ = OldThumbnail(img, maxSize);
                    var f = HalconImageHelper.ToThumbnailSource(img, maxSize);
                    if (f == null)
                    {
                        Check(title + "：快路径可用", false, "返回 null，跳过本组");
                        return;
                    }
                }

                var oldProbe = OldThumbnail(img, maxSize);
                var newProbe = HalconImageHelper.ToThumbnailSource(img, maxSize)!;
                Check("[PF-A2] 改造前/改造后缩略图尺寸一致",
                    oldProbe.PixelWidth == newProbe.PixelWidth && oldProbe.PixelHeight == newProbe.PixelHeight,
                    $"{oldProbe.PixelWidth}x{oldProbe.PixelHeight} vs {newProbe.PixelWidth}x{newProbe.PixelHeight}");

                var oldTimes = TimeLoop(rounds, () => OldThumbnail(img, maxSize));
                var newTimes = TimeLoop(rounds, () => HalconImageHelper.ToThumbnailSource(img, maxSize)!);

                // 顺手量托管分配（A2 的另一半收益：全尺寸 byte[] 不再进托管堆）
                long oldAlloc = AllocPerCall(rounds, () => OldThumbnail(img, maxSize));
                long newAlloc = AllocPerCall(rounds, () => HalconImageHelper.ToThumbnailSource(img, maxSize)!);

                Report("A2", oldTimes, newTimes, oldTimes.Length, newTimes.Length,
                    guardPct: 30,   // 基线差值 92%~98%，护栏留 30% 余量
                    allocOldPerCall: oldAlloc, allocNewPerCall: newAlloc);
            }
            finally
            {
                img.Dispose();
            }
        }

        /// <summary>改造前的缩略图实现（原样复刻：全尺寸转位图 → WPF 侧缩放）</summary>
        private static BitmapSource OldThumbnail(HImage image, int maxSize)
        {
            var full = HalconImageHelper.ToBitmapSource(image)!;
            int max = Math.Max(32, maxSize);
            double longest = Math.Max(full.PixelWidth, full.PixelHeight);
            if (longest <= 0) return full;

            double scale = Math.Min(1.0, max / longest);
            if (scale >= 0.999) return full;

            var scaled = new TransformedBitmap(full, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }

        private static HImage MakeImage(int w, int h, bool color)
        {
            if (!color)
            {
                var data = new byte[w * h];
                for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);
                var hd = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    var img = new HImage();
                    img.GenImage1("byte", w, h, hd.AddrOfPinnedObject());
                    return img;
                }
                finally { hd.Free(); }
            }
            else
            {
                var r = new byte[w * h]; var g = new byte[w * h]; var b = new byte[w * h];
                for (int i = 0; i < r.Length; i++) { r[i] = (byte)(i % 251); g[i] = (byte)(i % 97); b[i] = (byte)(i % 53); }
                var hr = GCHandle.Alloc(r, GCHandleType.Pinned);
                var hg = GCHandle.Alloc(g, GCHandleType.Pinned);
                var hb = GCHandle.Alloc(b, GCHandleType.Pinned);
                try
                {
                    var img = new HImage();
                    img.GenImage3("byte", w, h,
                        hr.AddrOfPinnedObject(), hg.AddrOfPinnedObject(), hb.AddrOfPinnedObject());
                    return img;
                }
                finally { hr.Free(); hg.Free(); hb.Free(); }
            }
        }

        // ==================================================================
        //  计时与统计
        // ==================================================================

        /// <summary>
        /// 计时循环：返回每轮耗时（毫秒，未排序）。
        /// Dispose 刻意放在计时窗口**之外**——两侧都要释放，属共有成本，不应计入差值。
        /// </summary>
        private static double[] TimeLoop(int rounds, Func<HImage> act)
        {
            var times = new double[rounds];
            for (int i = 0; i < rounds; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                HImage img = act();
                times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                img.Dispose();
            }
            return times;
        }

        private static double[] TimeLoop(int rounds, Func<BitmapSource> act)
        {
            var times = new double[rounds];
            for (int i = 0; i < rounds; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                var _ = act();
                times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            }
            return times;
        }

        private static long AllocPerCall(int rounds, Func<BitmapSource> act)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < rounds; i++) { var _ = act(); }
            return (GC.GetAllocatedBytesForCurrentThread() - before) / rounds;
        }

        private static void Report(
            string tag, double[] oldTimes, double[] newTimes,
            int oldRounds, int newRounds,
            double guardPct,
            long allocOldPerCall, long allocNewPerCall)
        {
            Array.Sort(oldTimes);
            Array.Sort(newTimes);

            double oldMed = Median(oldTimes), newMed = Median(newTimes);
            Console.WriteLine($"  改造前: 中位 {oldMed,8:F3} ms  均值 {Mean(oldTimes),8:F3}  最小 {oldTimes[0],8:F3}  最大 {oldTimes[^1],8:F3}");
            Console.WriteLine($"  改造后: 中位 {newMed,8:F3} ms  均值 {Mean(newTimes),8:F3}  最小 {newTimes[0],8:F3}  最大 {newTimes[^1],8:F3}");

            double saved = oldMed - newMed;
            double pct = oldMed > 0 ? saved / oldMed * 100.0 : 0;
            Console.WriteLine($"  → 每帧省 {saved,8:F3} ms（{pct,5:F1}%）｜30fps 省每秒 {saved * 30:F1} ms CPU");

            if (allocOldPerCall >= 0)
            {
                Console.WriteLine($"  托管分配/帧: 改造前 {FormatBytes(allocOldPerCall),10}  →  改造后 {FormatBytes(allocNewPerCall),10}"
                                + (allocOldPerCall > 0 ? $"（省 {(allocOldPerCall - allocNewPerCall) * 100.0 / allocOldPerCall:F1}%）" : ""));
                if (allocOldPerCall > 0)
                    Console.WriteLine($"  30fps 省每秒托管分配 {FormatBytes((long)((allocOldPerCall - allocNewPerCall) * 30))}");
            }

            // 回归护栏（断言故意松：防"被改回去"，不追求精确——见类注释）
            Check($"[{tag}] 回归护栏：改造后中位数至少快 {guardPct:F0}%",
                newMed <= oldMed * (1 - guardPct / 100.0),
                $"改造前 {oldMed:F3} ms → 改造后 {newMed:F3} ms（省 {pct:F1}%）");
        }

        /// <summary>抽查两张图若干采样点是否一致（全图比对太慢，抽样足够发现"通道/尺寸错了"这类问题）。
        /// 注意必须比 .I/.D 的**值**：HTuple.Equals 是引用语义，直接比恒为 false（实测踩过）。</summary>
        private static bool ImagesEqual(HImage a, HImage b)
        {
            try
            {
                a.GetImageSize(out int aw, out int ah);
                b.GetImageSize(out int bw, out int bh);
                if (aw != bw || ah != bh) return false;

                for (int row = 0; row < ah; row += Math.Max(1, ah / 8))
                {
                    for (int col = 0; col < aw; col += Math.Max(1, aw / 8))
                    {
                        HOperatorSet.GetGrayval(a, row, col, out HTuple va);
                        HOperatorSet.GetGrayval(b, row, col, out HTuple vb);
                        if (va.I != vb.I) return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double Median(double[] sorted)
            => sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;

        private static double Mean(double[] a)
        {
            double s = 0;
            foreach (var v in a) s += v;
            return s / a.Length;
        }

        private static string FormatBytes(long n)
        {
            if (n < 1024) return n + " B";
            if (n < 1024 * 1024) return (n / 1024.0).ToString("F1") + " KB";
            return (n / 1024.0 / 1024.0).ToString("F2") + " MB";
        }
    }
}
