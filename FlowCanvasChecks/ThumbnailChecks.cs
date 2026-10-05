using System;
using System.Runtime.InteropServices;
using Core.Events;
using HalconDotNet;
using VisionMaster.Helpers;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 缩略图快路径的契约断言（A2："HALCON 先等比缩小、再转位图"）。
    ///
    /// 缺陷原貌：为得到一张最长边 160px 的缩略图，先把**整张图**转成位图再缩放。
    /// 本方法在"每帧 × 每输出端口"上被调用，500 万像素彩图每帧约 15MB 托管分配，
    /// 高频采图下纯属浪费，随之而来的 GC 压力会让节拍不稳。
    ///
    /// 修法：<see cref="HalconImageHelper.ToThumbnailSource"/> 先在 HALCON 侧按最长边等比
    /// 缩小，再走既有的位图转换，转换量降到千分之几。不适用时（int4/real/非常规通道）
    /// 返回 null，由调用方回落全尺寸路径——行为与历史一致。
    ///
    /// 本断言锁死四件事
    /// ---------
    ///   ① 不影响精度：缩略图**只**服务图像集网格渲染，大图/导出/算法走的是全尺寸原图。
    ///      这里用"调用后源图像素值完全未变"来证明快路径没有改到算法输入。
    ///   ② 长宽比不拉伸：1920×1080 的缩略图仍是 16:9。
    ///   ③ 不放大：原图本来就小于上限时尺寸原样返回。
    ///   ④ 细线条不消失：区分"邻域插值"与"最近邻"的判别性测试——1 像素宽的线缩小 8 倍后
    ///      若仍可见，说明用的是邻域加权而不是"取一个点丢掉其余"。
    /// </summary>
    internal static class ThumbnailChecks
    {
        // ---- 测试图构造：直接从字节数组建 HImage（GenImage1/3 是复制语义，pin 只需覆盖调用期间）----

        private static HImage GrayFrom(byte[] data, int w, int h)
        {
            var hd = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var img = new HImage();
                img.GenImage1("byte", w, h, hd.AddrOfPinnedObject());
                return img;
            }
            finally { hd.Free(); }
        }

        private static HImage ColorFrom(byte[] r, byte[] g, byte[] b, int w, int h)
        {
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

        public static void Run()
        {
            Section("[TH] 缩略图快路径：先缩小再转换（不影响精度的根据）");

            // ---- ① 长宽比 + 缩小生效 + 源图未被改动 ----
            {
                // 1920×1080 灰度，全 200，仅 (0,0) 置 0 —— 作为"源图指纹"
                var data = new byte[1920 * 1080];
                for (int i = 0; i < data.Length; i++) data[i] = 200;
                data[0] = 0;

                var img = GrayFrom(data, 1920, 1080);
                try
                {
                    var thumb = HalconImageHelper.ToThumbnailSource(img, 160);
                    Check("[TH] 大图 → 缩略图非空（快路径生效）", thumb != null,
                        thumb == null ? "(null：快路径未生效，回落了全尺寸路径)" : $"{thumb.PixelWidth}x{thumb.PixelHeight}");

                    if (thumb != null)
                    {
                        Check("[TH] 缩略图最长边 ≤ 上限（160）",
                            Math.Max(thumb.PixelWidth, thumb.PixelHeight) <= 160,
                            $"{thumb.PixelWidth}x{thumb.PixelHeight}");

                        // 1920:1080 = 16:9 → 缩到最长边 160 应为 160×90
                        Check("[TH] 长宽比保持 16:9（不拉伸变形）",
                            thumb.PixelWidth == 160 && thumb.PixelHeight == 90,
                            $"期望 160x90，实测 {thumb.PixelWidth}x{thumb.PixelHeight}");
                    }

                    // 源图指纹未变：快路径不得改动输入图
                    HOperatorSet.GetGrayval(img, 0, 0, out HTuple g00);
                    HOperatorSet.GetGrayval(img, 100, 100, out HTuple g100);
                    Check("[TH] 快路径不改动源图（算法输入不受影响）",
                        g00.I == 0 && g100.I == 200,
                        $"(0,0)={g00.I}（期望 0），(100,100)={g100.I}（期望 200）");
                }
                finally { img.Dispose(); }
            }

            // ---- ② 不放大：原图小于上限时尺寸原样 ----
            {
                var data = new byte[64 * 48];
                for (int i = 0; i < data.Length; i++) data[i] = 128;

                var img = GrayFrom(data, 64, 48);
                try
                {
                    var thumb = HalconImageHelper.ToThumbnailSource(img, 160);
                    Check("[TH] 原图小于上限 → 不放大（尺寸原样 64x48）",
                        thumb != null && thumb.PixelWidth == 64 && thumb.PixelHeight == 48,
                        thumb == null ? "(null)" : $"{thumb.PixelWidth}x{thumb.PixelHeight}");
                }
                finally { img.Dispose(); }
            }

            // ---- ③ 彩色图走快路径，且通道语义不串 ----
            {
                // 纯红图（R=255, G=0, B=0）：缩略图必须仍是红——通道顺序错了会变蓝
                int n = 640 * 480;
                var rp = new byte[n]; var gp = new byte[n]; var bp = new byte[n];
                for (int i = 0; i < n; i++) { rp[i] = 255; gp[i] = 0; bp[i] = 0; }

                var img = ColorFrom(rp, gp, bp, 640, 480);
                try
                {
                    var thumb = HalconImageHelper.ToThumbnailSource(img, 160);
                    Check("[TH] 彩色图缩略图非空且尺寸正确（640x480 → 160x120）",
                        thumb != null && thumb.PixelWidth == 160 && thumb.PixelHeight == 120,
                        thumb == null ? "(null)" : $"{thumb.PixelWidth}x{thumb.PixelHeight}");

                    if (thumb != null)
                    {
                        var px = new byte[thumb.PixelWidth * thumb.PixelHeight * 3];
                        thumb.CopyPixels(px, thumb.PixelWidth * 3, 0);
                        int cx = ((thumb.PixelHeight / 2) * thumb.PixelWidth + thumb.PixelWidth / 2) * 3;
                        byte b = px[cx], g = px[cx + 1], r = px[cx + 2];   // Bgr24 排列
                        Check("[TH] 彩色缩略图通道顺序正确（纯红图仍是红，不是蓝）",
                            r > 200 && g < 60 && b < 60, $"B={b} G={g} R={r}（期望 R 高、G/B 低）");
                    }
                }
                finally { img.Dispose(); }
            }

            // ---- ④ 细线条可见性：邻域插值与最近邻的判别性测试 ----
            {
                // 64×64 全黑，每 4 行一条白线（共 16 条）。缩到 8×8（缩小 8 倍）：
                //   · 最近邻：每个目标像素只取一个源点，白线可能被整条跳过 → 接近全黑
                //   · 邻域插值：白线被平均进来 → 仍有明显亮点
                var data = new byte[64 * 64];
                for (int row = 0; row < 64; row += 4)
                    for (int col = 0; col < 64; col++)
                        data[row * 64 + col] = 255;

                var img = GrayFrom(data, 64, 64);
                try
                {
                    var thumb = HalconImageHelper.ToThumbnailSource(img, 8);
                    Check("[TH] 细线条测试图缩略图非空",
                        thumb != null, thumb == null ? "(null)" : $"{thumb.PixelWidth}x{thumb.PixelHeight}");

                    if (thumb != null)
                    {
                        var px = new byte[thumb.PixelWidth * thumb.PixelHeight];
                        thumb.CopyPixels(px, thumb.PixelWidth, 0);
                        byte max = 0;
                        foreach (var v in px) if (v > max) max = v;

                        // 判据：最大灰度明显高于全黑。最近邻若跳过白线，这里会接近 0。
                        Check("[TH] 细线条在缩略图中仍可见（用的是邻域插值，不是最近邻）",
                            max > 30, $"缩略图最大灰度={max}（最近邻跳过白线时会接近 0）");
                    }
                }
                finally { img.Dispose(); }
            }

            // ---- ⑤ int4/real 不走快路径（量程语义保护）----
            {
                HOperatorSet.GenImageConst(out HObject i4, "int4", 640, 480);
                var img = new HImage(i4);
                try
                {
                    var thumb = HalconImageHelper.ToThumbnailSource(img, 160);
                    Check("[TH] int4 不走快路径（返回 null，回落全尺寸以保住量程语义）",
                        thumb == null,
                        thumb == null ? "(null，正确)" : $"意外返回 {thumb.PixelWidth}x{thumb.PixelHeight}");
                }
                finally { img.Dispose(); }

                HOperatorSet.GenImageConst(out HObject realImg, "real", 640, 480);
                var img2 = new HImage(realImg);
                try
                {
                    var thumb = HalconImageHelper.ToThumbnailSource(img2, 160);
                    Check("[TH] real 不走快路径（返回 null）",
                        thumb == null,
                        thumb == null ? "(null，正确)" : $"意外返回 {thumb.PixelWidth}x{thumb.PixelHeight}");
                }
                finally { img2.Dispose(); }
            }

            // ---- ⑥ 端到端：图像集产出的缩略图受上限约束，且大图仍是全尺寸 ----
            {
                var settings = new VisionMaster.Services.AppSettingsService();
                var cfg = settings.Current.ImageGallery;
                var savedMax = cfg.ThumbnailMaxSize;
                var savedEnabled = cfg.Enabled;
                var savedRealtime = cfg.IncludeRealtimePreviews;
                try
                {
                    cfg.ThumbnailMaxSize = 160;
                    cfg.Enabled = true;
                    cfg.IncludeRealtimePreviews = true;   // 实时预览收录（与 [PC] 段同一前提）

                    var svc = new VisionMaster.Services.ImageCollectionService(settings, null!);
                    var plugin = new ThumbnailPublisherStub { InstanceName = "缩略图断言.发布方" };

                    var data = new byte[1920 * 1080];
                    for (int i = 0; i < data.Length; i++) data[i] = 100;

                    var img = GrayFrom(data, 1920, 1080);
                    try
                    {
                        plugin.PublishPreview(img, 1);

                        var frame = svc.Count > 0 ? svc.Frames[0] : null;
                        Check("[TH] 图像集端到端：缩略图已生成",
                            frame?.Thumbnail != null,
                            frame?.Thumbnail == null ? "(Thumbnail 为 null)" : $"{frame.Thumbnail.PixelWidth}x{frame.Thumbnail.PixelHeight}");

                        if (frame?.Thumbnail != null)
                        {
                            Check("[TH] 图像集缩略图最长边 ≤ 160 且保持 16:9（160x90）",
                                frame.Thumbnail.PixelWidth == 160 && frame.Thumbnail.PixelHeight == 90,
                                $"{frame.Thumbnail.PixelWidth}x{frame.Thumbnail.PixelHeight}");

                            Check("[TH] 缩略图已冻结（跨线程渲染的前提）",
                                frame.Thumbnail.IsFrozen, $"IsFrozen={frame.Thumbnail.IsFrozen}");
                        }

                        // 大图必须是全尺寸原图：这是"不影响精度"的正面证据
                        int fw = 0, fh = 0;
                        var fullOk = false;
                        if (frame?.Image != null)
                        {
                            try { frame.Image.GetImageSize(out fw, out fh); fullOk = fw == 1920 && fh == 1080; }
                            catch { /* 已失效 */ }
                        }
                        Check("[TH] 图集中的大图仍是全尺寸原图 1920x1080（算法/导出/大图走的是它）",
                            fullOk, fullOk ? "1920x1080" : $"{fw}x{fh}");
                    }
                    finally
                    {
                        img.Dispose();
                        svc.Clear();
                        ImagePreviewCaptureHub.Current = null;   // 不把钩子留给后续用例
                    }
                }
                finally
                {
                    cfg.ThumbnailMaxSize = savedMax;
                    cfg.Enabled = savedEnabled;
                    cfg.IncludeRealtimePreviews = savedRealtime;
                }
            }
        }

        /// <summary>只用来当发布方：PublishPreview 只读它的 InstanceName</summary>
        private sealed class ThumbnailPublisherStub : Core.Interfaces.VisionPluginBase
        {
            public override void RunAlgorithm(Core.Interfaces.IExecutionContext context) { }
        }
    }
}
