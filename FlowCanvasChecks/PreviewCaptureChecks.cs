using System;
using System.Threading;
using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 预览帧捕获时机的契约断言（"循环运行图不会被收纳"的回归）。
    ///
    /// 缺陷原貌：PublishPreview 经事件总线 BeginInvoke 到 UI 线程才拷图，而插件的输出图像
    /// 会在**下一轮开始时**被基类释放（VisionPluginBase.DisposePreviousRoundOutputs）。
    /// 循环运行时 UI 落后一轮，拷贝就发生在释放之后：
    ///   · 已完全释放 → IsInitialized() 为 false，被静默丢弃（画布空白、零日志）；
    ///   · 释放到一半 → HALCON #4060 "access to deleted region in copy_image"。
    /// 单次运行正常、循环一开必现，正是这个时序。
    ///
    /// 修法：宿主实现 IImagePreviewCapture，PublishPreview 在**发布线程**上先同步拷走。
    /// 本断言锁死的就是这条时序：发布调用返回时副本已到手；随后立刻释放源图（等价于下一轮）
    /// 也不影响副本；事件照旧派发，订阅者不因捕获而断链；异步兜底路径看到 Captured 会跳过。
    /// </summary>
    internal static class PreviewCaptureChecks
    {
        /// <summary>只用来当发布方：PublishPreview 只读它的 InstanceName</summary>
        private sealed class PreviewPublisherStub : VisionPluginBase
        {
            public override void RunAlgorithm(IExecutionContext context) { }
        }

        public static void Run()
        {
            Section("[PC] 预览帧捕获：发布线程同步拷贝（循环运行不丢帧的根据）");

            var settings = new VisionMaster.Services.AppSettingsService();
            var gallery = settings.Current.ImageGallery;
            gallery.Enabled = true;
            gallery.IncludeRealtimePreviews = true;   // 断言前提：打开实时收录

            var svc = new VisionMaster.Services.ImageCollectionService(settings, null!);
            Check("采集服务把自己注册为预览捕获钩子",
                ReferenceEquals(ImagePreviewCaptureHub.Current, svc),
                ImagePreviewCaptureHub.Current?.GetType().Name ?? "(null)");
            Check("实时收录开关可控（本用例前提）",
                gallery.Enabled && gallery.IncludeRealtimePreviews, "");

            var plugin = new PreviewPublisherStub { InstanceName = "捕获断言.发布方" };

            int delivered = 0;
            Action<ImageDisplayEvent<HImage>> subscriber = _ => Interlocked.Increment(ref delivered);
            GlobalEventBus.Subscribe(subscriber);
            try
            {
                var src = new HImage("byte", 8, 8);
                plugin.PublishPreview(src, 1);

                // 发布调用已返回 = 副本必须已经拷完；此刻释放源图，等价于"下一轮开始释放上一轮输出"
                src.Dispose();

                Check("发布调用返回时帧已收纳（拷贝发生在发布线程）",
                    svc.Count == 1, $"帧数={svc.Count}");

                var frame = svc.Count > 0 ? svc.Frames[0] : null;
                var copyUsable = false;
                if (frame?.Image != null)
                {
                    try { frame.Image.GetImageSize(out int _, out int _); copyUsable = true; } catch { /* 副本已失效 */ }
                }
                Check("源图随后被释放，副本仍可用（不再拷到已释放的图）",
                    copyUsable, frame == null ? "(没有帧)" : $"ViewIndex={frame.ViewIndex}");

                Check("事件照旧派发：订阅者仍有通知（捕获不吞事件）",
                    Volatile.Read(ref delivered) == 1, $"派发={delivered}");

                Check("同一帧只收纳一次（异步兜底路径看到 Captured 会跳过）",
                    svc.Count == 1, $"帧数={svc.Count}");
            }
            finally
            {
                GlobalEventBus.Unsubscribe(subscriber);
                svc.Clear();                              // 释放副本
                ImagePreviewCaptureHub.Current = null;    // 不把钩子留给后续用例
            }

            Check("清理后钩子已摘除（不影响后续用例）", ImagePreviewCaptureHub.Current == null, "");
        }
    }
}
