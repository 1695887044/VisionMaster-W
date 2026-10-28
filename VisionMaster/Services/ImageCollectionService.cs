using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    ///    事件订阅 <see cref="OnRealtimePreview"/> 只作无钩子时的兜底。
    ///    占用槽位随"收容"开关（<see cref="ImageGallerySettings.KeepInjectedHistory"/>）变：
    ///    默认关 = 每个（流程，窗口号）只留最新一张，新图顶掉旧图并当场释放；
    ///    打开 = 每帧各占一条，列表可回看历史。这张图说明什么由插件自己讲，不靠步骤名去猜。
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
    /// **清空范围**（2026-10-27）：窗口级（<c>ClearView</c>，默认自动口径）只清"本轮要显示的那一格"，
    /// 流程级（<c>ReplaceFlow</c>）只清"这条流程自己的所有窗口"——两者都**不碰其它流程在别的窗口的图**。
    /// 旧值 <c>ClearAll</c>（画布级）会在多流程并排时互相清屏，加载时自动迁移为窗口级。
    ///
    /// 归属：帧的流程身份由**发布事件自带**（引擎在每轮执行期间经
    /// <see cref="Core.Interfaces.PreviewScope"/> 注入），取不到才落伪流程桶——
    /// **不按"最后开轮的那条流程"猜**：多流程并发时那会把 A 的帧记成 B 的，
    /// 进而清错 B 的窗口（2026-10-27 现场：A 投窗口1、B 投窗口2 → 窗口2 被 A 反复清空）。
    /// 清空口径只有两种：窗口级（默认）/ 流程级，两者都不跨窗口误伤其它流程。
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
    ///
    /// 注入帧进 UI 前还要过一道**合并队列**（<see cref="_pendingFrames"/>）：每个（流程，窗口号）
    /// 只留最新一张，队列长度被"流程数 × 窗口数"封顶，排空动作走 Background 优先级。
    /// 这是"循环运行时界面卡死"的修法：旧实现每帧投一个 Normal 优先级动作，一旦每帧 UI 成本
    /// 超过帧间隔，队列永远排不空（Normal 比 Render/Input 都高），积压的动作还各钉一张全幅副本。
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
        /// 每轮开始的"待清空"记账（方案 A）：开轮只记序号、不真清——真清推迟到
        /// "本轮第一张新图到达"，与插入在同一个 UI 批次里完成，界面因此看不到
        /// "旧图 → 空 → 新图"的中间态（循环闪黑）。本轮没有任何新图 ⇒ 不清空，画布保持上一轮的图。
        ///
        /// 为什么记"每条流程的本轮序号"而不是一个 bool / 一个流程名集合：
        /// 多流程并发时"谁开的轮"必须各记各的，否则 A 开轮会把 B 的待清空状态一起消费掉
        /// （2026-10-27 现场：窗口 2 被 A 反复清空）。序号还让"每轮只清一次"可判定——
        /// 同一（流程，清空粒度）在本轮序号下只清一次，而不是每帧清一次。
        ///
        /// 流程线程写（OnFlowRunStarted）、UI 线程在批次里消费，加锁保护（多流程可并发）。
        /// 键的数量被"方案里的流程数 × 窗口数"封顶，不随运行时长增长。
        /// </summary>
        private readonly object _pendingClearGate = new();
        private readonly Dictionary<string, long> _roundToken = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Flow, int View), long> _clearedToken = new();
        private long _roundSequence;

        /// <summary>
        /// 待插入 UI 的帧（每个 (流程, 窗口号) 只留最新一张）。
        ///
        /// 为什么要合并：<see cref="Capture"/> 每帧都往 UI 线程投一个动作，动作里要做
        /// "清空 + 插入 + 裁剪 + 通知"，而选中帧变化会触发整幅 HALCON 重绘。循环运行时一旦
        /// "每帧 UI 成本 &gt; 帧间隔"，Normal 优先级队列就再也排不空（Normal 比 Render/Input 都高），
        /// 界面表现为卡死；积压的每个动作还各钉着一张全幅图像副本（500 万像素彩图约 15MB）。
        /// 合并后队列长度被"流程数 × 窗口数"封顶，中间帧在**进 UI 之前**就被顶掉并释放。
        ///
        /// 只在"不保留注入历史"时合并；收容打开时每帧都是一条记录，不能丢（走原路径）。
        /// </summary>
        private readonly object _pendingFrameGate = new();
        private readonly Dictionary<(string Flow, int View), (long Order, ImageFrame Frame)> _pendingFrames = new();
        private long _captureOrder;
        private int _uiDrainScheduled;
        private int _drainSuspended;

        /// <summary>
        /// 下一次允许排空的时间点（<see cref="Environment.TickCount64"/>）。0 = 不限速。
        ///
        /// 合并队列只封顶"队列长度"，不封顶"提交速率"：只要来帧快于 UI 提交，
        /// 排空动作就会一轮接一轮地重排，UI 线程永远不空闲（2026-10-27 真机：
        /// 7 条流程循环运行约 860 帧/秒，UI 线程被这一项吃掉大半，叠加日志洪水后界面失去响应）。
        /// 有积压时按 <see cref="MinCommitIntervalMs"/> 分片提交，窗口内到达的帧被合并队列顶掉——
        /// 只留最新一张本来就是本队列的约定，所以限速不改变可见语义。
        /// </summary>
        private long _nextCommitAllowedTick;

        /// <summary>已挂一个"到点再排"的延迟排空（0/1），防止每来一帧都排一个延迟动作</summary>
        private int _delayedDrainScheduled;

        /// <summary>有积压时两次提交之间的最小间隔（毫秒）：20Hz 足够让预览"看着是连续的"，
        /// 同时保证每个间隔之间渲染与输入都有机会插进来。</summary>
        private const int MinCommitIntervalMs = 50;

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

            // 报警图集目录在加"保留期"这一项之前只写不删（每张 ~15MB 量级）。
            // 放到后台跑：启动链上不该多一段磁盘遍历（与 R57/R79 同一口径）。
            System.Threading.Tasks.Task.Run(() => CleanupExpiredAlarmFrames());
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
                // 还没插进界面的待插帧也要一起丢掉：否则"清空画布"之后它们会再冒出来
                DiscardPendingFrames();
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
                var cfg = CurrentSettings();

                // 收容开关可能刚被翻过：已收下的注入帧要当场按新口径重整槽位，
                // 否则同一窗口会同时出现"旧键那条"与"新键这条"两张图（见 ImageFrame.Reslot）。
                ReslotInjectedFrames(cfg);

                TrimToLimits(cfg);
                OnChanged();
            });

            // 保留期可能刚被改小：立刻按新值清一次（后台，别占 UI 线程做目录遍历）
            System.Threading.Tasks.Task.Run(() => CleanupExpiredAlarmFrames());
        }

        /// <summary>
        /// 按当前收容开关给**已收下的注入帧**重算槽位键，并同步索引。
        ///
        /// 收容关（默认）：每个（流程，窗口号）只留最新一张——同槽的多帧按最旧优先淘汰；
        /// 收容开：每帧各占一条（唯一键），历史上都保留。
        ///
        /// 只动注入帧：兜底采集帧的位置是"流程+步骤+端口"，本来就被端口数封顶，
        /// 跟着收敛等于把可见的中间过程图白扔掉。
        /// </summary>
        private void ReslotInjectedFrames(ImageGallerySettings cfg)
        {
            var injected = _frames.Where(f => f.IsInjectedPreview).ToList();
            if (injected.Count == 0) return;

            // 先把这批帧从索引里摘掉，重算完再按新键重建——索引与帧必须一起改，
            // 只改一侧的后果是"图在集合里但覆盖打不中"，下一帧就会多出一条重复。
            foreach (var frame in injected)
            {
                if (_slotIndex.TryGetValue(frame.SlotKey, out var current) && ReferenceEquals(current, frame))
                    _slotIndex.Remove(frame.SlotKey);
            }

            if (cfg.KeepInjectedHistory)
            {
                // 收容开：帧之间不能再合并。已经是唯一键的保持不动，合并进来的分不到历史，
                // 仍按"一帧一条"处理（补一个唯一键，绝不退化成按窗口合并）。
                foreach (var frame in injected)
                {
                    frame.Reslot("\u0002" + Guid.NewGuid().ToString("N"));
                    _slotIndex[frame.SlotKey] = frame;
                }
                return;
            }

            // 收容关：按（流程，窗口号）收敛，每个位置只留序列号最大的那张
            foreach (var group in injected.GroupBy(PreviewSlotKey, StringComparer.Ordinal))
            {
                var ordered = group.OrderByDescending(f => f.Sequence).ToList();
                for (int i = 1; i < ordered.Count; i++)
                {
                    RemoveFrame(ordered[i]);
                }

                var keeper = ordered[0];
                keeper.Reslot(group.Key);
                _slotIndex[keeper.SlotKey] = keeper;
            }
        }

        /// <summary>
        /// 注入帧"不保留历史"时占的槽位：流程 + 窗口号。
        ///
        /// 为什么按窗口号而不是"流程+插件"：现场按窗口看画面（第 N 格 ↔ 窗口号 N），
        /// 同一窗口后来的图就该顶掉先来的——哪怕它们是不同插件发的（相机图 → 处理结果图，
        /// 这正是调参时最想看到的"同一路图在推进"）。按插件分会把同一格塞进多张，
        /// 于是格子显示的仍是"某个插件最后发的那张"，与"这个窗口最后一张"不是一回事。
        /// </summary>
        private static string PreviewSlotKey(ImageFrame frame)
            => "\u0003" + (frame.FlowName ?? string.Empty) + "\u0001" + frame.ViewIndex;

        /// <summary>
        /// 清理超过保留期的报警图集（程序目录 <c>AlarmFrames/yyyy-MM-dd/...</c>）。
        /// <b>0 或负数 = 永不清理</b>（与审计 / 报警历史 / 日志同一口径）。
        ///
        /// 三条边界：
        /// ① 只认 <c>yyyy-MM-dd</c> 形状的子目录——认不出的目录一律不动（那可能是用户自己放的东西）；
        /// ② 删除失败只记 Warn，不抛（磁盘被占用 / 权限不足不该让调用方崩）；
        /// ③ 判据用目录名里的日期而不是文件 mtime——拷来拷去会改 mtime，而目录名是落盘那一刻写死的。
        /// </summary>
        /// <param name="rootFolder">
        /// 报警图集根目录。null = 程序目录下的 <c>AlarmFrames</c>（真机路径）；
        /// 断言里显式指定临时目录，才不用往测试程序的输出目录里写文件。
        /// </param>
        public void CleanupExpiredAlarmFrames(string? rootFolder = null)
        {
            var retention = _settings.Current?.AlarmFrameRetentionDays
                            ?? AppConfigModel.DefaultAlarmFrameRetentionDays;

            CleanupExpiredAlarmFrames(
                retention,
                rootFolder ?? Path.Combine(AppContext.BaseDirectory, "AlarmFrames"),
                _log);
        }

        /// <summary>
        /// 清理的**纯函数形态**（公开是为了让断言直接调它，不必造一个会接管预览钩子的服务实例）。
        /// </summary>
        public static void CleanupExpiredAlarmFrames(int retentionDays, string rootFolder, ILogService? log = null)
        {
            var retention = retentionDays;
            if (retention <= 0) return;

            var root = rootFolder;
            if (!Directory.Exists(root)) return;

            var deadline = DateTime.Now.Date.AddDays(-retention);
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (!DateTime.TryParseExact(
                        Path.GetFileName(dir),
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var day))
                {
                    continue;
                }

                if (day >= deadline) continue;

                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex)
                {
                    log?.Warn($"[图像集] 清理过期报警图集失败（{dir}）：{ex.Message}");
                }
            }
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
        /// 一轮流程开始（流程线程）：按设置**登记待清空**。
        ///
        /// internal 而非 private：断言（FlowCanvasChecks）要按真实顺序驱动"多流程并发开轮"，
        /// 直接调本方法等价于 <see cref="FlowEngineService.FlowRunStarted"/> 事件（同一个入口）。
        ///
        /// 这里刻意**不立刻清空**：清完到本轮的图产出之间隔着整个采集+处理耗时，
        /// 循环节拍下这段空窗会被渲染看见（"黑屏 ↔ 图片"来回切换）。清空改由
        /// <see cref="ApplyPendingClearFor"/> 在本轮第一张新图到达时与插入同批完成。
        /// </summary>
        internal void OnFlowRunStarted(FlowSession session)
        {
            if (session == null) return;

            var cfg = CurrentSettings();
            if (!cfg.Enabled) return;

            var flowName = session.FlowName;
            if (string.IsNullOrEmpty(flowName)) return;

            // 只记"这条流程开了一轮"，不动任何全局"当前流程"——
            // 多流程并发时那个全局值必然是错的（最后写入者赢）。
            lock (_pendingClearGate)
            {
                _roundToken[flowName] = ++_roundSequence;
            }
        }

        /// <summary>
        /// UI 线程：把本轮登记的"待清空"落到实处——**必须与本次插入同批**（调用方在本方法返回后
        /// 立即 InsertOrReplace，中间不产生调度点），界面只会看到"旧图 → 新图"的一次切换。
        ///
        /// 清空范围 = 配置口径 × 本轮序号：
        ///  · <see cref="ImageGalleryRunStartMode.ClearView"/>（默认自动口径）→ 只清这一格；
        ///  · <see cref="ImageGalleryRunStartMode.ReplaceFlow"/> → 只清这条流程（它自己的所有窗口）。
        /// 两种口径都**绝不碰其它流程在别的窗口的图**——那正是"窗口2 被 A 清空"的根因。
        /// </summary>
        private void ApplyPendingClearFor(string flowName, int viewIndex)
        {
            if (string.IsNullOrEmpty(flowName)) return;

            var cfg = CurrentSettings();
            // 旧值 ClearAll（画布级）按窗口级处理：自动口径不允许跨窗口清屏（见枚举注释）。
            bool clearView = cfg.RunStartMode != ImageGalleryRunStartMode.ReplaceFlow;

            lock (_pendingClearGate)
            {
                // 不在流程的一轮里（伪流程桶 / 从未开轮）→ 不清：没有"上一轮"可言
                if (!_roundToken.TryGetValue(flowName, out var token)) return;

                // 记账粒度必须与清空粒度一致：
                //  · 窗口级 →（流程, 窗口）每轮各一次；
                //  · 流程级 →（流程, 0）每轮一次。流程级会连本流程其它窗口一起清，
                //    若按窗口记账，第二个窗口的第一帧会把第一个窗口刚插进去的新帧再清掉。
                var key = (flowName, clearView ? viewIndex : 0);
                if (_clearedToken.TryGetValue(key, out var done) && done == token) return;
                _clearedToken[key] = token;
            }

            if (clearView) RemoveFramesOfView(viewIndex);
            else RemoveFramesOfFlow(flowName);
        }

        /// <summary>
        /// 把一帧交进 UI 合并队列（见 <see cref="_pendingFrames"/> 的说明）。
        ///
        /// 拷贝已经在本方法返回前完成（发布线程上拷，见 <see cref="Capture"/>），这里只决定
        /// "什么时候把它插进界面"。收容打开时不能合并（每帧都是一条记录），走原路径。
        /// </summary>
        private void EnqueueFrame(ImageFrame frame, ImageGallerySettings cfg)
        {
            var dispatcher = Application.Current?.Dispatcher;

            // 已在 UI 线程（断言宿主直接调 Capture、或同线程宿主）：不存在"排队"的意义，
            // 直接提交——与合并前的 RunOnUi（CheckAccess 时同步执行）行为逐字一致。
            // 唯一例外是断言显式暂停排空：那时必须走队列，否则测不到"同一 (流程,窗口) 只留最新"。
            if ((dispatcher == null || dispatcher.CheckAccess())
                && Volatile.Read(ref _drainSuspended) == 0)
            {
                CommitFrame(frame);
                return;
            }

            if (cfg.KeepInjectedHistory)
            {
                // 收容打开：每帧都是一条记录，合并会丢历史 → 走原路径（一帧一个 UI 动作）
                RunOnUi(() => CommitFrame(frame));
                return;
            }

            var key = (frame.FlowName, frame.ViewIndex);
            long order = Interlocked.Increment(ref _captureOrder);
            ImageFrame? replaced = null;
            lock (_pendingFrameGate)
            {
                if (_pendingFrames.TryGetValue(key, out var old)) replaced = old.Frame;
                _pendingFrames[key] = (order, frame);
            }

            // 被顶掉的帧从没进过界面：直接释放，别让它跟着队列一起攒内存
            replaced?.Dispose();

            ScheduleUiDrain();
        }

        /// <summary>安排一次排空（同一时刻只挂一个动作）</summary>
        private void ScheduleUiDrain()
        {
            if (Volatile.Read(ref _drainSuspended) != 0) return;   // 断言用：模拟"UI 忙不过来"

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                // 断言宿主 / 退出链：没有消息循环，直接同步排空（行为与合并前一致）。
                // 这条路径**不受限速影响**——断言判的是队列内容，不是时序。
                if (Interlocked.Exchange(ref _uiDrainScheduled, 1) == 1) return;
                DrainPendingFrames();
                return;
            }

            // 限速窗口内：挂一个"到点再排"的动作，而不是立刻又跑一轮
            long wait = _nextCommitAllowedTick - Environment.TickCount64;
            if (wait > 0)
            {
                ScheduleDelayedDrain(dispatcher, (int)wait);
                return;
            }

            if (Interlocked.Exchange(ref _uiDrainScheduled, 1) == 1) return;

            // Background(4)：低于 Render(7)/Input(5)/Normal(9)。合并后每个 (流程,窗口) 只挂一个动作，
            // 不会再出现"Normal 洪水饿死重绘与输入"（图集重算就踩过 Background 饿死的坑，
            // 那时是因为插帧本身在 Normal 且每帧一个动作——合并之后这个前提没了）。
            dispatcher.BeginInvoke(new Action(DrainPendingFrames), DispatcherPriority.Background);
        }

        /// <summary>延迟排空：只在限速窗口内使用，同一时刻最多挂一个</summary>
        private void ScheduleDelayedDrain(Dispatcher dispatcher, int delayMs)
        {
            if (Interlocked.Exchange(ref _delayedDrainScheduled, 1) == 1) return;
            _ = RunDelayedDrainAsync(dispatcher, delayMs);
        }

        private async Task RunDelayedDrainAsync(Dispatcher dispatcher, int delayMs)
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
            catch
            {
                // 关停阶段的取消：下面照样尝试投递一次，投不进去由外层 try 兜住
            }

            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    Volatile.Write(ref _delayedDrainScheduled, 0);
                    if (Interlocked.Exchange(ref _uiDrainScheduled, 1) == 1) return;
                    DrainPendingFrames();
                }), DispatcherPriority.Background);
            }
            catch
            {
                // Dispatcher 已关闭：放开标志，别让 _delayedDrainScheduled 永久停在 1
                Volatile.Write(ref _delayedDrainScheduled, 0);
            }
        }

        /// <summary>
        /// UI 线程：把待插队列排空。一次只提交一批（按到达顺序），提交完若还有积压就
        /// 进限速窗口（见 <see cref="MinCommitIntervalMs"/>），把控制权还给渲染与输入。
        ///
        /// 为什么不再"连排若干轮直到排空"：那样一轮里的提交次数只受 CPU 限制，
        /// 来帧越快提交越多，UI 线程被自己占死——这正是"循环运行时界面卡死"的一半成因。
        ///
        /// 异常隔离：单帧提交失败只丢那一帧（释放副本 + 记 Warn），整批照跑；
        /// 任何逃逸异常都必须把 <see cref="_uiDrainScheduled"/> 放回 0——否则它永久停在 1，
        /// 之后**再也不会有任何帧进界面**（图窗静默停止更新，最难查的一类）。
        /// </summary>
        private void DrainPendingFrames()
        {
            try
            {
                DrainPendingFramesCore();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _uiDrainScheduled, 0);
                _log?.Warn($"[图像集] 帧入 UI 合并排空失败（已放开队列，下一帧继续）：{ex.Message}");
            }
        }

        private void DrainPendingFramesCore()
        {
            List<(long Order, ImageFrame Frame)> batch;
            lock (_pendingFrameGate)
            {
                if (_pendingFrames.Count == 0)
                {
                    // 与入队同锁复位：入队方要么已被本次取走，要么会在复位后重新排一次（不丢唤醒）
                    Volatile.Write(ref _uiDrainScheduled, 0);
                    return;
                }

                batch = _pendingFrames.Values.ToList();
                _pendingFrames.Clear();
            }

            foreach (var item in batch.OrderBy(x => x.Order))
            {
                try
                {
                    CommitFrame(item.Frame);
                }
                catch (Exception ex)
                {
                    item.Frame.Dispose();
                    _log?.Warn($"[图像集] 帧入界面失败（已丢弃该帧，其余照常）：{ex.Message}");
                }
            }

            lock (_pendingFrameGate)
            {
                if (_pendingFrames.Count == 0)
                {
                    Volatile.Write(ref _uiDrainScheduled, 0);
                    return;
                }
            }

            // 还有积压 = 来帧快于 UI 提交：下一次排空推迟到限速窗口之后。
            // 窗口内到达的新帧由合并队列顶掉（同一 (流程,窗口) 只留最新一张），
            // 所以限速不改变可见语义，只是把"提交多少帧"从"来多少提交多少"变成"每 50ms 一批"。
            _nextCommitAllowedTick = Environment.TickCount64 + MinCommitIntervalMs;

            Volatile.Write(ref _uiDrainScheduled, 0);
            ScheduleUiDrain();
        }

        /// <summary>
        /// 把一帧真正写进集合（UI 线程）。清空与本帧插入**同一批**完成（中间无调度点）——
        /// 界面只会看到"旧图 → 新图"，看不到中间空态（循环闪黑）。
        /// </summary>
        private void CommitFrame(ImageFrame frame)
        {
            var cfg = CurrentSettings();
            ApplyPendingClearFor(frame.FlowName, frame.ViewIndex);
            InsertOrReplace(frame, cfg);
            TrimToLimits(cfg);
            OnChanged();
        }

        /// <summary>丢掉还没插入界面的待插帧（清空画布时用；调用方负责在 UI 线程执行）</summary>
        private void DiscardPendingFrames()
        {
            List<ImageFrame> dropped;
            lock (_pendingFrameGate)
            {
                if (_pendingFrames.Count == 0) return;
                dropped = _pendingFrames.Values.Select(v => v.Frame).ToList();
                _pendingFrames.Clear();
            }

            foreach (var frame in dropped) frame.Dispose();
        }

        // ── 断言辅助（internal + InternalsVisibleTo FlowCanvasChecks）──

        /// <summary>待插队列长度（断言"队列有界"用）</summary>
        internal int PendingFrameCountForTest
        {
            get { lock (_pendingFrameGate) return _pendingFrames.Count; }
        }

        /// <summary>
        /// 暂停 UI 排空（断言用）：模拟"UI 线程忙不过来"，让待插帧在队列里堆着，
        /// 从而可判定"同一 (流程,窗口) 只留最新一张"。Dispose 时恢复并补一次排空。
        /// </summary>
        internal IDisposable SuspendUiDrainForTest()
        {
            Interlocked.Increment(ref _drainSuspended);
            return new DrainSuspension(this);
        }

        private sealed class DrainSuspension : IDisposable
        {
            private ImageCollectionService? _owner;

            public DrainSuspension(ImageCollectionService owner) => _owner = owner;

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null) return;
                _owner = null;

                if (Interlocked.Decrement(ref owner._drainSuspended) == 0)
                    owner.ScheduleUiDrain();
            }
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

                    // 归属窗口号取插件自己声明的"显示窗口号"——**不能一律塞第 1 格**：
                    // 多流程并跑时每条流程各发自己的窗口，全塞 1 会让第 1 格被所有流程刷满、
                    // 其余各格永远空着。插件没声明 / 声明为 0（不显示）时才回落到窗口 1，
                    // 因为兜底的语义是"至少让这张图有地方出现"，而不是"按不显示的意图丢弃"。
                    var viewIndex = ReadDisplayViewIndex(plugin);
                    var frame = BuildFrame(session.FlowName, stepName, port.Key, image,
                        title: null, info: null, annotations: null, uniqueSlot: false,
                        viewIndex: viewIndex > 0 ? viewIndex : 1, cfg);
                    if (frame != null) frames.Add(frame);
                }
            }

            return frames;
        }

        /// <summary>
        /// 读插件声明的"显示窗口号"（1~9；0 = 不显示 / 未声明）。读不到一律返回 0。
        ///
        /// 为什么要在宿主侧读、而不是让插件报上来：兜底采集的时机是"一轮跑完之后遍历输出端口"，
        /// 那一刻插件已经退出 RunAlgorithm，没有上下文可问；窗口号只存在于插件的配置属性上。
        ///
        /// 两种声明形态都要认（R84 同类坑：同一个语义在两个程序集里各写一套）：
        /// ① <c>[StepConfig] public int DisplayViewIndex</c> —— 普通属性，插件自己读它发布；
        /// ② <c>InputPort&lt;int&gt; DisplayViewIndex</c> —— 端口，值在 <c>IPort.Value</c> 里。
        /// 都不是则返回 0，由调用方回落到窗口 1。
        /// </summary>
        private static int ReadDisplayViewIndex(IVisionPlugin plugin)
        {
            const string name = "DisplayViewIndex";
            if (plugin == null) return 0;

            try
            {
                // ① 端口形态：值可能被变量/上游链接改过，以运行期值为准
                if (plugin.Inputs != null && plugin.Inputs.TryGetValue(name, out var port)
                    && port?.Value != null)
                {
                    return Convert.ToInt32(port.Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                // ② 普通属性形态：插件发布时读的就是它，兜底采集必须与它一致
                var prop = plugin.GetType().GetProperty(
                    name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(int) && prop.CanRead)
                    return (int)prop.GetValue(plugin);
            }
            catch
            {
                // 读窗口号失败不该让整轮兜底采集作废（其它端口的图仍然有收集价值）
            }

            return 0;
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

            // 归属到**发布方自己声明的流程**（引擎在每轮执行期间经 PreviewScope 注入）。
            // 取不到就落伪流程桶：绝不回退成"最后开轮的那条流程"——多流程并发时会清错别人的窗口。
            var flow = ResolveFlowName(e.FlowName);
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

            // 交给 UI 合并队列：同一 (流程,窗口) 只留最新一张，队列长度被"流程数×窗口数"封顶。
            // 清空与插入仍在同一个 UI 批次里完成（见 CommitFrame），界面看不到中间空态。
            EnqueueFrame(frame, cfg);
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

            // 归属到**发布方自己声明的流程**（同 Capture：不按"最后开轮的那条流程"猜）
            var flow = ResolveFlowName(e.FlowName);
            var source = string.IsNullOrWhiteSpace(e.PluginName) ? $"视图{e.ViewIndex}" : e.PluginName!;

            var frame = BuildFrame(flow, source, string.Empty, e.Image,
                e.Title, e.InfoRows, e.Annotations, uniqueSlot: true, viewIndex: e.ViewIndex, cfg);
            if (frame == null) return;

            // 交给 UI 合并队列：同一 (流程,窗口) 只留最新一张，队列长度被"流程数×窗口数"封顶。
            // 清空与插入仍在同一个 UI 批次里完成（见 CommitFrame），界面看不到中间空态。
            EnqueueFrame(frame, cfg);
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

                // 缩略图无条件生成：收容关时条子虽然不显示，但用户随时可能在设置里打开收容，
                // 那时条子要显示的是**已收下的这批帧**——省掉这一步，打开收容后条子里
                // 会全是"无预览"占位（那些帧永远补不回来）。宁可每帧多一次缩小，
                // 也不要把"Thumbnail 是否为空"变成依赖配置的隐式契约。
                var thumbnail = MakeThumbnail(copy, cfg.ThumbnailMaxSize);

                // 注入帧的槽位键随"收容"开关变：
                //   收容开 = 唯一键（每帧一条，列表能回看历史）；
                //   收容关 = 流程 + 窗口号（同窗口后来的图顶掉先来的，旧的当场释放）。
                // 兜底采集帧（uniqueSlot=false）仍按"流程+步骤+端口"占槽，由端口数天然封顶。
                string? slotKey = null;
                if (uniqueSlot && !cfg.KeepInjectedHistory)
                    slotKey = "\u0003" + (flowName ?? string.Empty) + "\u0001" + (viewIndex <= 0 ? 1 : viewIndex);
                else if (uniqueSlot)
                    slotKey = "\u0002" + Guid.NewGuid().ToString("N");

                return new ImageFrame(slotKey)
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
                    Annotations = annotations,
                    IsInjectedPreview = uniqueSlot
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
        /// 缩略图：优先走"HALCON 先等比缩小、再转位图"的快路径（<see cref="HalconImageHelper.ToThumbnailSource"/>），
        /// 不适用时回落"全尺寸转位图再缩放"。
        ///
        /// 为什么快路径值当
        /// ---------
        /// 本方法在**每帧 × 每输出端口**上被调用：全尺寸路径要为一张 160px 的缩略图把整张图
        /// （500 万像素彩图约 15MB）搬进托管堆，随即被 GC 回收。先缩小再转，转换量降到千分之几。
        ///
        /// 为什么不影响视觉算法
        /// ---------
        /// 缩略图**只用于图像集网格渲染**（<c>ImageGallery.xaml</c> 的 <c>&lt;Image Source="{Binding Thumbnail}"&gt;</c>）；
        /// 大图显示、导出、标注、取色走的都是 <see cref="ImageFrame.Image"/>（全尺寸原图）。
        /// 这条路径与算法结果没有任何交集。
        ///
        /// 回落的情况（int4/real、非常规通道、缩放失败）
        /// ---------
        /// int4/real 的显示依赖本图 min/max 拉伸，先缩小会让缩略图量程与全图不一致，故不走快路径；
        /// 这类图（视差 / 深度 / 浮点结果图）本就少见，回落全尺寸路径行为与历史完全一致。
        ///
        /// 两条路径产出的位图都已 <c>Freeze()</c>（缩略图要跨线程读）。
        /// </summary>
        private static BitmapSource? MakeThumbnail(HImage image, int maxSize)
        {
            // 快路径：HALCON 侧先等比缩小再转换（只影响缩略图渲染，不参与任何视觉算法）
            var fast = HalconImageHelper.ToThumbnailSource(image, maxSize);
            if (fast != null) return fast;

            // 回落：全尺寸转换 + WPF 侧缩放（int4/real 等），与历史行为逐字一致
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
            // 本轮只有兜底图、没有插件注入时，这条路径就是那"第一张新图"）。
            //
            // 逐帧应用（而不是只看 frames[0]）：兜底帧可能落在不同窗口，窗口级口径要各清各的；
            // "每轮只清一次"由 ApplyPendingClearFor 的序号记账保证，同一流程不会重复清。
            foreach (var frame in frames)
            {
                ApplyPendingClearFor(frame.FlowName, frame.ViewIndex);
                InsertOrReplace(frame, cfg);
            }

            TrimToLimits(cfg);
        }

        private void InsertOrReplace(ImageFrame frame, ImageGallerySettings cfg)
        {
            frame.Sequence = ++_sequence;

            // 默认（不保留历史帧）：同一槽位后来者覆盖并释放旧帧。
            // 两类帧的“收容”开关不同：注入帧看 KeepInjectedHistory，兜底采集帧看 KeepFrameHistory。
            // 不能统一用 KeepFrameHistory：只打开它时，注入帧的槽键仍是“流程+窗口号”，
            // 却因为“保留历史”不替换，同一窗口会留下多条重复帧。
            bool keepHistory = frame.IsInjectedPreview ? cfg.KeepInjectedHistory : cfg.KeepFrameHistory;
            if (!keepHistory && _slotIndex.TryGetValue(frame.SlotKey, out var existing) && existing != null)
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

        /// <summary>
        /// 清掉某一格（窗口号）里的全部帧——**含其它流程在该格的图**。
        /// 这是"每轮只看本窗口本轮图"的实现；别的窗口一张都不动。
        /// </summary>
        private void RemoveFramesOfView(int viewIndex)
        {
            if (viewIndex <= 0) return;

            for (int i = _frames.Count - 1; i >= 0; i--)
            {
                if (_frames[i].ViewIndex != viewIndex) continue;
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

        /// <summary>
        /// 帧归属的流程名：优先用发布事件自带的（引擎在每轮执行期间经
        /// <see cref="Core.Interfaces.PreviewScope"/> 注入）。
        ///
        /// 取不到 = 不在流程的一轮里发布（插件自建线程/定时器、非流程宿主）→ 落伪流程桶。
        /// **绝不猜**"最后开轮的那条流程"：猜错会把别人的帧当成自己的清掉，
        /// 那正是多流程并排时"窗口互相清屏"的成因。
        /// </summary>
        private static string ResolveFlowName(string? flowName)
            => string.IsNullOrWhiteSpace(flowName) ? RealtimeFlowName : flowName!;

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
