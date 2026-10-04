using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Core.Events;
using Core.Halcon.Helpers;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using VisionMaster.Helpers;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 画布采集服务：把"流程运行期间产生的图像"收成一个列表，供视觉图像页的画布（图像列表）显示。
    ///
    /// 两个采集源（为什么是两条而不是一条）
    /// ---------
    /// ① **插件主动注入**（主）：<see cref="IImagePreviewCapture.Capture"/> 在**发布线程**上同步
    ///    把图拷走（关键：这张图活不过发布方的下一轮，异步到 UI 线程再拷就晚了——见该接口的说明），
    ///    事件订阅 <see cref="OnRealtimePreview"/> 只作无钩子时的兜底。逐条进列表，不做槽位合并。
    ///    这张图说明什么由插件自己讲，不靠步骤名去猜。
    /// ② **运行完成兜底枚举**（辅，默认关）：订阅 <see cref="FlowEngineService.FlowRunCompleted"/>，
    ///    每跑完一轮遍历 <c>session.ExecutionEngine.PluginLookup</c> 里每个插件的 <c>Outputs</c>，
    ///    把没被主动注入的 <see cref="HImage"/> 端口也收进来（这些图没有说明信息）。由
    ///    <see cref="ImageGallerySettings.CollectAllOutputPorts"/> 门控。
    ///
    /// 每轮开始怎么清（<see cref="ImageGallerySettings.RunStartMode"/>）
    /// ---------
    /// 订阅 <see cref="FlowEngineService.FlowRunStarted"/>，在本轮开始时**只登记"待清空"**，
    /// 真正的清空推迟到"本轮第一张新图到达"时、与插入在同一个 UI 批次里完成（见
    /// <see cref="ApplyPendingClearFor"/>）。为什么不能在本轮开始就清：清完到本轮的图产出之间隔着
    /// 整个采集+处理耗时（循环节拍下几十毫秒），这段空窗会被渲染看见——现场表现就是
    /// "显示窗口在黑屏和图片之间来回切换"。推迟后语义一字不改（仍是"每轮只看最新一轮"），
    /// 但界面只会看到"旧图 → 新图"的一次切换。
    /// 本轮**没有任何新图**（采集失败/未产出）⇒ 不清空，画布保持上一轮的图。
    ///
    /// 归属：插件注入的图记到"当前正在跑的流程"（<c>_currentFlowName</c>），
    /// 所以"覆盖本流程"清的是该流程上一轮的图；「清空整张画布」则是首帧到达时全清。
    ///
    /// 所有权与生命周期（本类是全库唯一"持有并释放 HImage 副本"的地方）
    /// ---------
    /// · 采集时一律 <c>CopyImage()</c> 出**独立副本**，绝不复用插件端口对象（插件下一轮会改它）。
    /// · 淘汰 / 覆盖 / 清空时由本类 <see cref="ImageFrame.Dispose"/> 释放，显示端（HalconBase）只读不释放。
    /// · 张数上限（总张数 / 单流程张数）在每次写入后统一裁剪，按最旧优先覆盖；上限来自
    ///   <see cref="ImageGallerySettings"/>，0 或负数 = 不限。
    ///
    /// 线程约定
    /// ---------
    /// 引擎回调在流程线程上，<see cref="ObservableCollection{T}"/> 只能在 UI 线程改，所以所有集合写入
    /// 统一经 <see cref="RunOnUi"/> 封送；封送前先在流程线程把图**拷贝**好（下一轮开始前必须拷完，
    /// 否则会把插件正在写的那张图拷出撕裂帧）。插件注入帧的拷贝时机同理——由
    /// <see cref="IImagePreviewCapture"/> 保证在发布线程上完成（循环运行时的必现缺陷，见其注释）。
    /// </summary>
    public sealed class ImageCollectionService : IImagePreviewCapture
    {
        /// <summary>实时预览帧归属的伪流程名（与真实流程名不可能重名，因为它带全角括号）</summary>
        public const string RealtimeFlowName = "（实时预览）";

        /// <summary>全局单例引用：供纯 code-behind 的视图（如 ImageView）直接取用，避免改造成 MVVM</summary>
        public static ImageCollectionService? Instance { get; private set; }

        private readonly AppSettingsService _settings;
        private readonly ILogService? _log;

        private readonly ObservableCollection<ImageFrame> _frames = new();
        private readonly Dictionary<string, ImageFrame> _slotIndex = new(StringComparer.Ordinal);
        private long _sequence;
        private bool _truncatedNotified;

        /// <summary>累计收录帧数（诊断日志用：首帧 + 每 200 帧一条）</summary>
        private long _capturedFrames;

        /// <summary>累计"发布线程上图像就已失效"的跳过数（正常应恒为 0；>0 即发布方提前释放了图）</summary>
        private long _deadSourceSkips;

        /// <summary>
        /// 本轮登记的"待清空"（方案 A）：本轮开始只登记、不真清——真清推迟到"本轮第一张新图到达"，
        /// 与插入在同一个 UI 批次里完成，界面因此看不到"旧图 → 空 → 新图"的中间态（循环闪黑）。
        /// 本轮没有任何新图 ⇒ 不清空，画布保持上一轮的图。
        /// 流程线程写（OnFlowRunStarted / Capture）、UI 线程在批次里消费，加锁保护（多流程可并发）。
        /// </summary>
        private readonly object _pendingClearGate = new();
        private readonly HashSet<string> _pendingClearFlows = new(StringComparer.Ordinal);
        private bool _pendingClearAll;

        /// <summary>
        /// 当前正在跑的流程名（由"一轮开始"事件写入）。插件注入的图要归属到它，
        /// 这样"覆盖本流程"才能把上一轮注入的图一并替换掉。不在流程里发布时为 null。
        /// </summary>
        private volatile string? _currentFlowName;

        public ImageCollectionService(AppSettingsService settings, FlowEngineService engine, ILogService? log = null)
        {
            Instance = this;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log;

            Frames = new ReadOnlyObservableCollection<ImageFrame>(_frames);

            if (engine != null)
            {
                engine.FlowRunCompleted += OnFlowRunCompleted;
                engine.FlowRunStarted += OnFlowRunStarted;
            }

            GlobalEventBus.Subscribe<ImageDisplayEvent<HImage>>(OnRealtimePreview);

            // 注册为预览捕获钩子：插件一发布，就先在**发布线程**上把图拷走。
            // 只订阅事件是不够的——事件是 BeginInvoke 到 UI 线程派发的，而这张图
            // 活不过发布方的下一轮（基类会释放上一轮的输出），循环运行时 UI 落后一轮
            // 就会拷到已释放的图（现场："单次运行正常，循环一开图不会被收纳"）。
            ImagePreviewCaptureHub.Current = this;
        }

        /// <summary>采集到的全部帧（只读视图，UI 直接绑定）</summary>
        public ReadOnlyObservableCollection<ImageFrame> Frames { get; }

        /// <summary>当前帧数（供标题/状态显示）</summary>
        public int Count => _frames.Count;

        /// <summary>当前生效的画布配置（只读快照；宿主据此决定提示文案、是否采集等）</summary>
        public ImageGallerySettings Settings => CurrentSettings();

        /// <summary>集合内容发生变化（新增/覆盖/清空/裁剪）</summary>
        public event EventHandler? Changed;

        /// <summary>清空图像集（释放全部图像副本）</summary>
        public void Clear()
        {
            RunOnUi(() =>
            {
                foreach (var frame in _frames) frame.Dispose();
                _frames.Clear();
                _slotIndex.Clear();
                OnChanged();
            });
        }

        /// <summary>
        /// 配置改完立即生效：按新上限裁剪一次（模式/开关在下次采集时自然生效）。
        /// 由系统参数设置弹窗在保存成功后调用。
        /// </summary>
        public void ApplySettingsNow()
        {
            RunOnUi(() =>
            {
                TrimToLimits(CurrentSettings());
                OnChanged();
            });
        }

        /// <summary>
        /// 把当前图集整批落盘（报警留存 / 外部调用）。
        /// 默认目录：程序目录 <c>AlarmFrames/yyyy-MM-dd/{reason}_{HHmmss}/</c>，按流程分子目录、PNG 格式。
        /// 非 <paramref name="force"/> 时受 <see cref="ImageGallerySettings.SaveFramesOnAlarm"/> 开关约束。
        ///
        /// 线程：先在 UI 线程取一份帧快照（集合归 UI 线程所有），写盘丢到后台任务——
        /// 报警回调落在轮询/节拍线程上，一次写几十上百个文件不能占着它。
        /// 后台写盘期间若某帧被上限淘汰释放，该帧写失败会被逐张吞掉（见 <see cref="ImageExporter"/>），
        /// 不影响其余帧——这是"省一次全量拷贝"换来的可接受代价。
        /// </summary>
        public void SaveSnapshot(string reason, string? rootFolder = null, bool force = false)
        {
            var cfg = CurrentSettings();
            if (!force && !cfg.SaveFramesOnAlarm) return;

            RunOnUi(() =>
            {
                var snapshot = _frames.Where(f => f.Image != null).ToList();
                if (snapshot.Count == 0) return;

                var root = rootFolder ?? Path.Combine(
                    AppContext.BaseDirectory, "AlarmFrames",
                    DateTime.Now.ToString("yyyy-MM-dd"),
                    $"{SanitizeForPath(reason)}_{DateTime.Now:HHmmss}");

                System.Threading.Tasks.Task.Run(() =>
                {
                    var saved = ImageExporter.SaveFrames(snapshot, root, groupByFlow: true);
                    _log?.Warn($"[图像集] 已留存 {saved}/{snapshot.Count} 张（{reason}）到 {root}");
                });
            });
        }

        private static string SanitizeForPath(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "未命名";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            return sb.ToString();
        }

        #region 采集

        /// <summary>
        /// 一轮流程开始（流程线程）：记下"当前流程"供注入图归属，并按设置**登记待清空**。
        ///
        /// 这里刻意**不立刻清空**：清完到本轮的图产出之间隔着整个采集+处理耗时，
        /// 循环节拍下这段空窗会被渲染看见（"黑屏 ↔ 图片"来回切换）。清空改由
        /// <see cref="ApplyPendingClearFor"/> 在本轮第一张新图到达时与插入同批完成。
        /// </summary>
        private void OnFlowRunStarted(FlowSession session)
        {
            if (session == null) return;

            _currentFlowName = session.FlowName;

            var cfg = CurrentSettings();
            if (!cfg.Enabled) return;

            var flowName = session.FlowName;

            lock (_pendingClearGate)
            {
                if (cfg.RunStartMode == ImageGalleryRunStartMode.ClearAll)
                {
                    _pendingClearAll = true;
                }
                else if (!string.IsNullOrEmpty(flowName))
                {
                    _pendingClearFlows.Add(flowName);
                }
            }
        }

        /// <summary>
        /// UI 线程：把本轮登记的"待清空"落到实处——**必须与本次插入同批**（调用方在本方法返回后
        /// 立即 InsertOrReplace，中间不产生调度点），界面只会看到"旧图 → 新图"的一次切换。
        /// </summary>
        private void ApplyPendingClearFor(string flowName)
        {
            bool clearAll;
            bool clearFlow = false;

            lock (_pendingClearGate)
            {
                clearAll = _pendingClearAll;
                if (clearAll)
                {
                    _pendingClearAll = false;
                    _pendingClearFlows.Clear();
                }
                else if (!string.IsNullOrEmpty(flowName))
                {
                    clearFlow = _pendingClearFlows.Remove(flowName);
                }
            }

            if (clearAll)
            {
                // 「清空整张画布」：本轮第一张新图到达时才清（含其它流程上一轮的图）
                Clear();
                return;
            }

            if (clearFlow) RemoveFramesOfFlow(flowName);
        }

        /// <summary>运行完成回调（流程线程）：先拷好图，再封送到 UI 线程写集合</summary>
        private void OnFlowRunCompleted(FlowSession session)
        {
            var cfg = CurrentSettings();
            if (!cfg.Enabled || !cfg.CollectAllOutputPorts) return;

            List<ImageFrame> harvested;
            try
            {
                harvested = Harvest(session, cfg);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[图像集] 采集流程「{session?.FlowName}」输出图像失败：{ex.Message}");
                return;
            }

            if (harvested.Count == 0) return;

            RunOnUi(() =>
            {
                ApplyHarvest(harvested, CurrentSettings());
                OnChanged();
            });
        }

        /// <summary>
        /// 枚举会话里所有步骤的所有 HImage 输出端口，逐个拷成帧。
        /// 步骤名反查沿用 <see cref="FlowOutputCollector"/> 的同一口径：StepID 是不随命名口径变化的身份。
        /// </summary>
        private List<ImageFrame> Harvest(FlowSession session, ImageGallerySettings cfg)
        {
            var frames = new List<ImageFrame>();
            var engine = session?.ExecutionEngine;
            if (engine?.PluginLookup == null) return frames;

            var stepNameById = new Dictionary<Guid, string>();
            if (session.Blueprints != null)
            {
                foreach (var step in session.Blueprints)
                {
                    if (step == null) continue;
                    var name = step.StepName;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!stepNameById.ContainsKey(step.StepID)) stepNameById[step.StepID] = name;
                }
            }

            foreach (var pair in engine.PluginLookup)
            {
                var plugin = pair.Value;
                if (plugin?.Outputs == null) continue;

                var stepName = stepNameById.TryGetValue(pair.Key, out var n)
                    ? n
                    : pair.Key.ToString("N").Substring(0, 8);

                foreach (var port in plugin.Outputs)
                {
                    if (port.Value?.Value is not HImage image) continue;
                    if (!SafeIsInitialized(image)) continue;

                    // 端口兜底采集的图没有窗口号 → 归到第 1 格（多格布局下不至于凭空消失）
                    var frame = BuildFrame(session.FlowName, stepName, port.Key, image,
                        title: null, info: null, annotations: null, uniqueSlot: false, viewIndex: 1, cfg);
                    if (frame != null) frames.Add(frame);
                }
            }

            return frames;
        }

        /// <summary>
        /// 预览帧的发布线程捕获（<see cref="IImagePreviewCapture"/> 实现）——插件注入图的主路径。
        ///
        /// 与 <see cref="OnRealtimePreview"/> 的分工：
        ///   本方法在**发布线程**上先跑（PublishPreview 调用返回之前），把图拷成独立副本；
        ///   异步回调随后在 UI 线程跑，看到 <see cref="ImageDisplayEvent{T}.Captured"/> 直接跳过，
        ///   保证同一帧只收纳一次。判据两边完全一致，谁都不会漏。
        ///
        /// 为什么拷贝必须在发布线程：这张图通常就是插件的输出端口值，基类在**下一轮开始时**
        /// 释放上一轮的输出。UI 一旦落后一轮（循环运行、大图、高节拍），异步回调再拷就是
        /// 已释放的图：轻则 HALCON #4060 报错，重则 IsInitialized()==false 被静默丢弃——
        /// 现场表现正是"单次运行正常，循环一开图不会被收纳"。
        /// </summary>
        public void Capture(ImageDisplayEvent<HImage> e)
        {
            var cfg = CurrentSettings();
            if (!cfg.Enabled || !cfg.IncludeRealtimePreviews) return;
            if (e == null || e.Image == null || e.ViewIndex <= 0) return;
            if (!SafeIsInitialized(e.Image))
            {
                // 发布线程上就失效：正常不该发生（图在这一刻必然有效），
                // 真出现说明发布方在 publish 之前就释放了图。绝不能静默——这条静默路径
                // 正是"循环运行图不进画布"能潜伏很久的原因（见 IImagePreviewCapture 的说明）。
                var deadSkips = Interlocked.Increment(ref _deadSourceSkips);
                if (deadSkips <= 3 || deadSkips % 200 == 0)
                    _log?.Warn($"[图像集] 跳过 {deadSkips} 帧：发布时图像已失效（发布方提前释放？）");
                return;
            }

            // 归属到"当前正在跑的流程"（没有流程在执行时才落到伪流程桶）：
            // 于是"覆盖本流程"能把该流程上一轮注入的图一并替换掉
            var flow = _currentFlowName ?? RealtimeFlowName;
            var source = string.IsNullOrWhiteSpace(e.PluginName) ? $"视图{e.ViewIndex}" : e.PluginName!;

            var frame = BuildFrame(flow, source, string.Empty, e.Image,
                e.Title, e.InfoRows, e.Annotations, uniqueSlot: true, viewIndex: e.ViewIndex, cfg);

            // 不论成败都置位：拷贝失败已由 BuildFrame 留痕，让异步路径再试一次只会再失败一次
            e.Captured = true;
            if (frame == null) return;

            // 收录诊断：首帧 + 每 200 帧一条（循环运行约数秒一条）。排查"图不进画布"时，
            // 先看这条日志就能区分"没收录"与"收录了但界面没刷新"，不必再靠猜
            var captured = Interlocked.Increment(ref _capturedFrames);
            if (captured == 1 || captured % 200 == 0)
                _log?.Info($"[图像集] 已收录 {captured} 帧（最近 {flow}·{source} "
                          + $"{frame.Width}x{frame.Height} → 窗口 {e.ViewIndex}）");

            RunOnUi(() =>
            {
                // 方案 A：本轮的清空与本帧插入**同一批**完成（中间无调度点）——
                // 界面只会看到"旧图 → 新图"，看不到中间空态（循环闪黑）
                ApplyPendingClearFor(frame.FlowName);
                InsertOrReplace(frame, cfg);
                TrimToLimits(cfg);
                OnChanged();
            });
        }

        /// <summary>
        /// 插件注入回调（UI 线程）：注入的图是画布列表的主角——**每次注入就是一条新记录**，
        /// 不做"按视图号占槽"的合并，否则同一插件连续注入只会留下最后一张。
        ///
        /// 本回调是"捕获钩子未注册"时的兜底路径；钩子已注册时 <see cref="Capture"/> 已在发布线程
        /// 收走这一帧（置位 Captured），这里直接跳过——那时源图可能已被下一轮释放。
        /// </summary>
        private void OnRealtimePreview(ImageDisplayEvent<HImage> e)
        {
            if (e?.Captured == true) return;

            var cfg = CurrentSettings();
            if (!cfg.Enabled || !cfg.IncludeRealtimePreviews) return;
            if (e?.Image == null || e.ViewIndex <= 0) return;
            if (!SafeIsInitialized(e.Image)) return;

            // 归属到"当前正在跑的流程"（没有流程在执行时才落到伪流程桶）：
            // 于是"覆盖本流程"能把该流程上一轮注入的图一并替换掉
            var flow = _currentFlowName ?? RealtimeFlowName;
            var source = string.IsNullOrWhiteSpace(e.PluginName) ? $"视图{e.ViewIndex}" : e.PluginName!;

            var frame = BuildFrame(flow, source, string.Empty, e.Image,
                e.Title, e.InfoRows, e.Annotations, uniqueSlot: true, viewIndex: e.ViewIndex, cfg);
            if (frame == null) return;

            RunOnUi(() =>
            {
                // 方案 A：本轮的清空与本帧插入**同一批**完成（中间无调度点）——
                // 界面只会看到"旧图 → 新图"，看不到中间空态（循环闪黑）
                ApplyPendingClearFor(frame.FlowName);
                InsertOrReplace(frame, cfg);
                TrimToLimits(cfg);
                OnChanged();
            });
        }

        /// <summary>把源图拷成独立副本 + 生成缩略图，组装成一帧（失败返回 null，不抛）</summary>
        private ImageFrame? BuildFrame(
            string flowName,
            string stepName,
            string portName,
            HImage source,
            string? title,
            IReadOnlyList<ImageInfoRow>? info,
            IReadOnlyList<MeasureAnnotation>? annotations,
            bool uniqueSlot,
            int viewIndex,
            ImageGallerySettings cfg)
        {
            HImage? copy = null;
            try
            {
                source.GetImageSize(out int width, out int height);
                copy = source.CopyImage();
                var thumbnail = MakeThumbnail(copy, cfg.ThumbnailMaxSize);

                // 注入帧给唯一槽位键：列表要一条条列出来，不能被同名槽位合并掉
                return new ImageFrame(uniqueSlot ? "\u0002" + Guid.NewGuid().ToString("N") : null)
                {
                    FlowName = flowName ?? string.Empty,
                    StepName = stepName ?? string.Empty,
                    PortName = portName ?? string.Empty,
                    ViewIndex = viewIndex <= 0 ? 1 : viewIndex,
                    Width = width,
                    Height = height,
                    Timestamp = DateTime.Now,
                    Title = title,
                    InfoRows = info,
                    Image = copy,
                    Thumbnail = thumbnail,
                    Annotations = annotations
                };
            }
            catch (Exception ex)
            {
                copy?.Dispose();
                _log?.Warn($"[图像集] 拷贝图像失败（{flowName}.{stepName}.{portName}）：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 缩略图：先整体转一次位图（唯一转换入口 <see cref="HalconImageHelper"/>），
        /// 再按最长边缩放。刻意不在 HALCON 侧缩放——那要处理单/多通道与像素类型分支，
        /// 而 WPF 侧的 <see cref="TransformedBitmap"/> 一律适用，且结果冻结后可跨线程读。
        /// 代价是转换时会短暂占用一张全尺寸位图（一张 500 万像素彩图约 15MB，随即被 GC 回收）。
        /// </summary>
        private static BitmapSource? MakeThumbnail(HImage image, int maxSize)
        {
            var full = HalconImageHelper.ToBitmapSource(image);
            if (full == null) return null;

            int max = Math.Max(32, maxSize);
            double longest = Math.Max(full.PixelWidth, full.PixelHeight);
            if (longest <= 0) return full;

            double scale = Math.Min(1.0, max / longest);
            if (scale >= 0.999) return full;

            try
            {
                var scaled = new TransformedBitmap(full, new ScaleTransform(scale, scale));
                scaled.Freeze();
                return scaled;
            }
            catch
            {
                return full;
            }
        }

        #endregion

        #region 集合写入与裁剪（UI 线程）

        private void ApplyHarvest(List<ImageFrame> frames, ImageGallerySettings cfg)
        {
            // 方案 A：本轮的清空与首批兜底图**同一批**完成（同 Capture：清空绝不单独先于图出现；
            // 本轮只有兜底图、没有插件注入时，这条路径就是那"第一张新图"）
            if (frames.Count > 0)
                ApplyPendingClearFor(frames[0].FlowName);

            foreach (var frame in frames)
                InsertOrReplace(frame, cfg);

            TrimToLimits(cfg);
        }

        private void InsertOrReplace(ImageFrame frame, ImageGallerySettings cfg)
        {
            frame.Sequence = ++_sequence;

            // 默认（不保留历史帧）：同一"流程 + 步骤 + 端口"占一个槽，后来者覆盖并释放旧帧。
            // 于是图集张数被"输出端口数"封顶，循环运行也不会越跑越多。
            if (!cfg.KeepFrameHistory && _slotIndex.TryGetValue(frame.SlotKey, out var existing) && existing != null)
            {
                int index = _frames.IndexOf(existing);
                _slotIndex[frame.SlotKey] = frame;
                if (index >= 0) _frames[index] = frame;
                else _frames.Add(frame);
                existing.Dispose();
                return;
            }

            _frames.Add(frame);
            _slotIndex[frame.SlotKey] = frame;
        }

        private void RemoveFramesOfFlow(string flowName)
        {
            for (int i = _frames.Count - 1; i >= 0; i--)
            {
                var frame = _frames[i];
                if (!string.Equals(frame.FlowName, flowName, StringComparison.Ordinal)) continue;
                RemoveFrameAt(i);
            }
        }

        private void TrimToLimits(ImageGallerySettings cfg)
        {
            bool trimmed = false;

            if (cfg.PerFlowLimit > 0)
            {
                foreach (var group in _frames.GroupBy(f => f.FlowName, StringComparer.Ordinal).ToList())
                {
                    var list = group.ToList();
                    if (list.Count <= cfg.PerFlowLimit) continue;

                    foreach (var old in list.OrderBy(f => f.Sequence).Take(list.Count - cfg.PerFlowLimit).ToList())
                    {
                        RemoveFrame(old);
                        trimmed = true;
                    }
                }
            }

            if (cfg.TotalLimit > 0 && _frames.Count > cfg.TotalLimit)
            {
                foreach (var old in _frames.OrderBy(f => f.Sequence).Take(_frames.Count - cfg.TotalLimit).ToList())
                {
                    RemoveFrame(old);
                    trimmed = true;
                }
            }

            // 只在第一次触发上限时提醒一次：持续刷屏的日志在现场等于噪音
            if (trimmed && !_truncatedNotified)
            {
                _truncatedNotified = true;
                _log?.Warn($"[图像集] 已达张数上限（总 {cfg.TotalLimit} / 单流程 {cfg.PerFlowLimit}），开始按最旧优先覆盖。可在「系统参数设置」调整。");
            }
        }

        private void RemoveFrame(ImageFrame frame)
        {
            int index = _frames.IndexOf(frame);
            if (index >= 0) RemoveFrameAt(index);
        }

        private void RemoveFrameAt(int index)
        {
            var frame = _frames[index];
            _frames.RemoveAt(index);
            if (_slotIndex.TryGetValue(frame.SlotKey, out var current) && ReferenceEquals(current, frame))
                _slotIndex.Remove(frame.SlotKey);
            frame.Dispose();
        }

        #endregion

        #region 基础设施

        private ImageGallerySettings CurrentSettings()
            => _settings.Current.ImageGallery ?? new ImageGallerySettings();

        private static bool SafeIsInitialized(HImage image)
        {
            try { return image != null && image.IsInitialized(); }
            catch { return false; }
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }
            dispatcher.BeginInvoke(action);
        }

        #endregion
    }
}
