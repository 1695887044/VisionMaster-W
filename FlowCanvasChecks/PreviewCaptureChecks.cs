using System;
using System.Linq;
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

            /// <summary>声明窗口号（[StepConfig] 属性形态：普通 int，不进端口表）</summary>
            public int DisplayViewIndex { get; set; } = 1;
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

            RunHarvestWindowChecks(settings);
            RunMultiFlowChecks();
            RunUiCoalescingChecks();
        }

        /// <summary>
        /// 兜底采集（<c>CollectAllOutputPorts</c>）的**窗口归属**。
        ///
        /// 缺陷原貌：<c>Harvest</c> 把所有输出端口的图一律塞进窗口 1。单流程时看不出来
        /// （反正只有一条流程在发），但多流程各占一个窗口的现场，一开这个开关就会让
        /// 第 1 格被所有流程的图轮番刷满、其余各格永远空着——而那正是"打开兜底反而更乱"。
        ///
        /// 修法：读插件自己声明的 <c>DisplayViewIndex</c>（两种声明形态都要认，见
        /// <c>ImageCollectionService.ReadDisplayViewIndex</c>），读不到才回落窗口 1。
        /// 这里直接用真插件实例跑一遍 Harvest，钉住"声明几号就进几号格"。
        /// </summary>
        private static void RunHarvestWindowChecks(VisionMaster.Services.AppSettingsService settings)
        {
            Section("[HW] 兜底采集：窗口号取插件声明值（不是一律塞窗口 1）");

            var gallery = settings.Current.ImageGallery;
            bool savedEnabled = gallery.Enabled;
            bool savedCollectAll = gallery.CollectAllOutputPorts;
            bool savedRealTime = gallery.IncludeRealtimePreviews;

            var svc = new VisionMaster.Services.ImageCollectionService(settings, null!);
            try
            {
                gallery.Enabled = true;
                gallery.CollectAllOutputPorts = true;
                gallery.IncludeRealtimePreviews = false;   // 只测兜底路径，别让注入帧混进来

                // 造一个"窗口号声明为 5"的插件，挂一张图到它的输出端口上
                var plugin = new HarvestStub { InstanceName = "兜底断言.窗口5", DisplayViewIndex = 5 };
                using var img = new HImage("byte", 16, 12);
                plugin.Image.Value = img;

                // Harvest 是私有方法，直接反射调它（不经引擎回调：兜底采集的判据是
                // "遍历 PluginLookup 的输出端口"，把这一步单独钉住即可）
                var session = new VisionMaster.Models.FlowSession { FlowName = "兜底流程" };
                var session2 = new VisionMaster.Models.FlowSession { FlowName = "兜底流程2" };
                try
                {
                    session.ExecutionEngine = new VisionMaster.Models.CompiledFlow(
                        new List<VisionMaster.Models.CompiledNode>(),
                        new Dictionary<Guid, IVisionPlugin> { [Guid.NewGuid()] = plugin },
                        new Dictionary<Guid, VisionMaster.Models.CompiledNode>(),
                        new Dictionary<Guid, List<Guid>>());

                    var harvest = typeof(VisionMaster.Services.ImageCollectionService)
                        .GetMethod("Harvest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    var frames = (List<Core.Halcon.Models.ImageFrame>)harvest.Invoke(svc, new object[] { session, gallery })!;

                    var picked = frames.FirstOrDefault();
                    Check("[HW] 插件声明窗口 5 → 兜底帧归窗口 5（不是窗口 1）",
                        picked != null && picked.ViewIndex == 5,
                        picked == null ? "(没有产出帧)" : $"ViewIndex={picked.ViewIndex}");

                    // 反向：没声明窗口号的插件（属性不存在）必须回落窗口 1，不能归到 0 而丢掉
                    var plain = new HarvestNoWindowStub { InstanceName = "兜底断言.未声明" };
                    using var img2 = new HImage("byte", 16, 12);
                    plain.Image.Value = img2;

                    session2.ExecutionEngine = new VisionMaster.Models.CompiledFlow(
                        new List<VisionMaster.Models.CompiledNode>(),
                        new Dictionary<Guid, IVisionPlugin> { [Guid.NewGuid()] = plain },
                        new Dictionary<Guid, VisionMaster.Models.CompiledNode>(),
                        new Dictionary<Guid, List<Guid>>());

                    var frames2 = (List<Core.Halcon.Models.ImageFrame>)harvest.Invoke(svc, new object[] { session2, gallery })!;
                    var picked2 = frames2.FirstOrDefault();
                    Check("[HW] 未声明窗口号 → 回落窗口 1（不落 0、不丢图）",
                        picked2 != null && picked2.ViewIndex == 1,
                        picked2 == null ? "(没有产出帧)" : $"ViewIndex={picked2.ViewIndex}");

                    foreach (var f in frames.Concat(frames2)) f.Dispose();
                }
                finally
                {
                    // FlowSession 持有 CancellationTokenSource / SemaphoreSlim，且 Dispose 会连带释放
                    // PluginLookup 里的插件实例——断言里必须自己收，泄漏会一直攒到进程退出。
                    session.Dispose();
                    session2.Dispose();
                }
            }
            finally
            {
                gallery.Enabled = savedEnabled;
                gallery.CollectAllOutputPorts = savedCollectAll;
                gallery.IncludeRealtimePreviews = savedRealTime;
                svc.Clear();
                ImagePreviewCaptureHub.Current = null;
            }
        }

        /// <summary>
        /// 多流程并发：帧归属与清空范围（2026-10-27 现场："A 投窗口1、B 投窗口2 → 窗口2 被 A 清空"）。
        ///
        /// 缺陷原貌（两条都在旧实现里）：
        ///  ① 归属靠**单例全局** <c>_currentFlowName</c>（"最后开轮的那条流程"）。A、B 并发时
        ///     A 发布的帧被记成 B 的，于是"只清本流程"把 B 的窗口清掉——正是现场症状；
        ///  ② <c>ClearAll</c>（旧"每轮清空整张画布"）是画布级口径，A 开一轮就能把 B 的窗口一起清掉。
        ///
        /// 修法：流程身份由 <see cref="PreviewScope"/> 在每轮执行期间注入发布事件；
        /// 自动清空只允许"流程级 / 窗口级"，且每条流程每轮各清一次（不是每帧清一次）。
        /// 本组断言按真实操作顺序驱动：先开轮、再发布，并故意让 B 的开轮夹在 A 的开轮与发布之间
        /// ——这正是旧实现会归属错的那一段时序。
        /// </summary>
        private static void RunMultiFlowChecks()
        {
            Section("[MC] 多流程并发：帧归属按发布流程、清空不越界");

            var settings = new VisionMaster.Services.AppSettingsService();
            var gallery = settings.Current.ImageGallery ??= new VisionMaster.Models.ImageGallerySettings();

            bool savedEnabled = gallery.Enabled;
            bool savedRealtime = gallery.IncludeRealtimePreviews;
            bool savedInjectedHistory = gallery.KeepInjectedHistory;
            var savedMode = gallery.RunStartMode;

            var svc = new VisionMaster.Services.ImageCollectionService(settings, null!);
            var publisher = new PreviewPublisherStub { InstanceName = "并发断言.发布方" };
            var flowA = new VisionMaster.Models.FlowSession { FlowName = "并发流程A" };
            var flowB = new VisionMaster.Models.FlowSession { FlowName = "并发流程B" };

            try
            {
                gallery.Enabled = true;
                gallery.IncludeRealtimePreviews = true;
                gallery.KeepInjectedHistory = false;   // 每格只留一张（现场默认）

                // ── 第 1 轮：A→窗口1、B→窗口2（各自开轮后发布）──────────────────
                gallery.RunStartMode = VisionMaster.Models.ImageGalleryRunStartMode.ReplaceFlow;

                svc.OnFlowRunStarted(flowA);
                PublishInScope(publisher, flowA.FlowName, 1);
                svc.OnFlowRunStarted(flowB);
                PublishInScope(publisher, flowB.FlowName, 2);

                Check("[MC1] 两条流程各占一个窗口：帧归属各是各的",
                    FrameOf(svc, 1)?.FlowName == flowA.FlowName
                    && FrameOf(svc, 2)?.FlowName == flowB.FlowName,
                    DescribeFrames(svc));

                // ── 第 2 轮：A、B 都先开轮，再让 A 发布 ──────────────────────────
                // 旧实现按"最后开轮的流程"归属 → A 的帧被记成 B → 清空 B 的帧（窗口2 被清）
                svc.OnFlowRunStarted(flowA);
                svc.OnFlowRunStarted(flowB);
                PublishInScope(publisher, flowA.FlowName, 1);

                Check("[MC2] A 开一轮不得清掉 B 在另一个窗口的图（回归：窗口2 被 A 影响）",
                    FrameOf(svc, 2)?.FlowName == flowB.FlowName,
                    DescribeFrames(svc));

                // 归属错了会在同一窗口多留一张（新帧换了槽位，旧帧没被替换）——所以按"窗口1 恰好一张"判，
                // 而不是"取第一张看名字"：后者在旧帧仍在时会假绿。
                var view1Frames = svc.Frames.Where(f => f.ViewIndex == 1).ToList();
                Check("[MC3] A 的帧仍归 A，且同一窗口只留本轮一张（不按“最后开轮者”归属）",
                    view1Frames.Count == 1 && view1Frames[0].FlowName == flowA.FlowName,
                    DescribeFrames(svc));

                // ── 窗口级口径：只清目标窗口，另一个窗口不动 ────────────────────
                gallery.RunStartMode = VisionMaster.Models.ImageGalleryRunStartMode.ClearView;
                svc.Clear();

                svc.OnFlowRunStarted(flowB);
                PublishInScope(publisher, flowB.FlowName, 1);   // 跨窗口对照：B 占窗口1
                svc.OnFlowRunStarted(flowA);
                PublishInScope(publisher, flowA.FlowName, 2);   // A 占窗口2

                svc.OnFlowRunStarted(flowA);                    // A 新开一轮
                PublishInScope(publisher, flowA.FlowName, 1);   // 投窗口1

                Check("[MC4] 窗口级口径只清目标窗口：窗口2 的帧不受影响",
                    FrameOf(svc, 2) != null,
                    DescribeFrames(svc));
                Check("[MC5] 窗口级口径确实清掉了目标窗口里别的流程的旧帧",
                    FrameOf(svc, 1)?.FlowName == flowA.FlowName && svc.Count == 2,
                    DescribeFrames(svc));

                // ── 流程级口径：每条流程每轮只清一次，不误删本轮新帧 ─────────────
                gallery.RunStartMode = VisionMaster.Models.ImageGalleryRunStartMode.ReplaceFlow;
                svc.Clear();

                svc.OnFlowRunStarted(flowA);
                PublishInScope(publisher, flowA.FlowName, 1);
                PublishInScope(publisher, flowA.FlowName, 2);

                Check("[MC6] 流程级口径每轮只清一次：同一轮两个窗口的新帧都在",
                    FrameOf(svc, 1) != null && FrameOf(svc, 2) != null,
                    DescribeFrames(svc));

                // ── 反向：没有流程作用域时不猜"最后开轮的流程" ──────────────────
                svc.Clear();
                svc.OnFlowRunStarted(flowA);
                using (var img = new HImage("byte", 8, 8))
                    publisher.PublishPreview(img, 1);           // 故意不设 PreviewScope

                Check("[MC7] 无流程作用域 → 落伪流程桶（绝不猜最后开轮的流程）",
                    FrameOf(svc, 1)?.FlowName == VisionMaster.Services.ImageCollectionService.RealtimeFlowName,
                    DescribeFrames(svc));
            }
            finally
            {
                gallery.Enabled = savedEnabled;
                gallery.IncludeRealtimePreviews = savedRealtime;
                gallery.KeepInjectedHistory = savedInjectedHistory;
                gallery.RunStartMode = savedMode;
                svc.Clear();
                ImagePreviewCaptureHub.Current = null;
                flowA.Dispose();
                flowB.Dispose();
            }
        }

        /// <summary>
        /// 批 2（2026-10-27）：帧入 UI 合并——队列有界、同一 (流程,窗口) 只留最新。
        ///
        /// 缺陷原貌：每发布一帧就 `BeginInvoke` 一个"清空+插入+裁剪+通知"的动作（Normal 优先级，
        /// 高于 Render/Input）。循环运行时一旦每帧 UI 成本超过帧间隔，队列永远排不空 → 界面卡死；
        /// 积压的每个动作还各钉着一张全幅图像副本（500 万像素彩图约 15MB）。
        ///
        /// 修法：发布线程拷完图先进"每个 (流程,窗口) 只留最新一张"的待插表，UI 忙时后来的帧
        /// 直接顶掉先来的（先来的当场释放），队列长度被"流程数 × 窗口数"封顶。
        /// 断言用 `SuspendUiDrainForTest` 模拟"UI 忙不过来"，判定的是队列内容而不是时序。
        /// </summary>
        private static void RunUiCoalescingChecks()
        {
            Section("[UI] 帧入 UI 合并：队列有界、只留最新一张");

            var settings = new VisionMaster.Services.AppSettingsService();
            var gallery = settings.Current.ImageGallery ??= new VisionMaster.Models.ImageGallerySettings();

            bool savedEnabled = gallery.Enabled;
            bool savedRealtime = gallery.IncludeRealtimePreviews;
            bool savedInjectedHistory = gallery.KeepInjectedHistory;

            var svc = new VisionMaster.Services.ImageCollectionService(settings, null!);
            var publisher = new PreviewPublisherStub { InstanceName = "合并断言.发布方" };

            try
            {
                gallery.Enabled = true;
                gallery.IncludeRealtimePreviews = true;
                gallery.KeepInjectedHistory = false;   // 只有"不保留历史"时才合并

                using (svc.SuspendUiDrainForTest())
                {
                    PublishSized(publisher, "合并流程", 1, 8, 8);
                    PublishSized(publisher, "合并流程", 1, 16, 16);
                    PublishSized(publisher, "合并流程", 1, 24, 24);

                    Check("[UI1] UI 忙时同一 (流程,窗口) 只留最新一帧（待插队列不随帧率增长）",
                        svc.PendingFrameCountForTest == 1,
                        $"待插={svc.PendingFrameCountForTest}");
                    Check("[UI2] 合并期间一帧都还没插进去（不会半批可见）",
                        svc.Count == 0, $"已插={svc.Count}");
                }

                Check("[UI3] 恢复排空后只插入最后一张（中间帧被丢弃并释放）",
                    svc.Count == 1
                    && svc.Frames[0].Width == 24 && svc.Frames[0].Height == 24,
                    svc.Count == 0
                        ? "没有帧"
                        : $"帧数={svc.Count} 尺寸={svc.Frames[0].Width}x{svc.Frames[0].Height}");

                // 收容打开时不能合并（每帧都是一条记录）——反向钉住"不要为了省事一刀切"
                gallery.KeepInjectedHistory = true;
                svc.Clear();
                using (svc.SuspendUiDrainForTest())
                {
                    PublishSized(publisher, "合并流程", 1, 8, 8);
                    PublishSized(publisher, "合并流程", 1, 16, 16);
                    PublishSized(publisher, "合并流程", 1, 24, 24);
                }
                Check("[UI4] 收容打开时不合并：三帧都在（历史不能丢）",
                    svc.Count == 3, $"帧数={svc.Count}");
            }
            finally
            {
                gallery.Enabled = savedEnabled;
                gallery.IncludeRealtimePreviews = savedRealtime;
                gallery.KeepInjectedHistory = savedInjectedHistory;
                svc.Clear();
                ImagePreviewCaptureHub.Current = null;
            }
        }

        /// <summary>在指定流程作用域内发布一张 w×h 的图（尺寸用来判定"哪一帧被留下了"）</summary>
        private static void PublishSized(PreviewPublisherStub publisher, string flowName, int viewIndex, int width, int height)
        {
            using (PreviewScope.Begin(flowName))
            using (var img = new HImage("byte", width, height))
            {
                publisher.PublishPreview(img, viewIndex);
            }
        }

        /// <summary>在指定流程作用域内发布一帧（等价于流程线程里 RunAlgorithm 调 PublishPreview）</summary>
        private static void PublishInScope(PreviewPublisherStub publisher, string flowName, int viewIndex)
        {
            using (PreviewScope.Begin(flowName))
            using (var img = new HImage("byte", 8, 8))
            {
                publisher.PublishPreview(img, viewIndex);
            }
        }

        private static Core.Halcon.Models.ImageFrame? FrameOf(VisionMaster.Services.ImageCollectionService svc, int viewIndex)
            => svc.Frames.FirstOrDefault(f => f.ViewIndex == viewIndex);

        private static string DescribeFrames(VisionMaster.Services.ImageCollectionService svc)
            => svc.Frames.Count == 0
                ? "帧=0"
                : "帧=" + string.Join("；", svc.Frames.Select(f => $"{f.FlowName}→窗口{f.ViewIndex}"));

        /// <summary>兜底采集用：带 <c>DisplayViewIndex</c> 输出的桩插件。
        /// 属性形态与真插件一致（<c>[StepConfig] int</c> 是普通属性、不进端口表，
        /// 所以窗口号只能由宿主反射读取——见 ReadDisplayViewIndex）。</summary>
        private sealed class HarvestStub : VisionPluginBase
        {
            public int DisplayViewIndex { get; set; } = 1;

            public Core.Interfaces.OutputPort<HImage> Image { get; } = new("Image", "图像");

            public override void RunAlgorithm(IExecutionContext context) { }
        }

        /// <summary>兜底采集用：**不声明**窗口号的桩插件（判"回落窗口 1"）</summary>
        private sealed class HarvestNoWindowStub : VisionPluginBase
        {
            public Core.Interfaces.OutputPort<HImage> Image { get; } = new("Image", "图像");

            public override void RunAlgorithm(IExecutionContext context) { }
        }
    }
}
