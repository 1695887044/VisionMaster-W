using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「系统设置」弹窗：软件级偏好的唯一入口（正文由 <c>CardPropertyGrid</c> 按
    /// <see cref="SystemSettingsDraft"/> 上的 <c>[SuperDisplay]</c> 反射生成）。
    ///
    /// 【合并了什么】原先两个弹窗：
    ///   · 「运行窗口设置」—— 依附主窗口 / 独立窗口 + 落在哪块屏；
    ///   · 「系统参数设置」—— 空闲自动登出、审计 / 报警历史保留、画布（图集）。
    /// 它们写的是同一份东西（程序目录 <c>AppConfig.json</c>），口径也逐条相同：
    /// 草稿 + 确定才落盘、只把真变了的项写进审计、写盘失败留在窗上让人重试。
    /// 拆成两个弹窗的代价是把"读配置 → 编辑 → 算变更 → Save → 失败留窗 → 落审计"
    /// 这整条链抄了两份，而且每加一项都要动一次 XAML。
    ///
    /// 【为什么正文用反射属性面板】字段清单只存在于草稿类上：新增一项 = 加一个属性，
    /// 视图一行都不用改。代价与对策见 <see cref="SystemSettingsDraft"/> 的类型注释。
    ///
    /// 【为什么不用 EasyDialog.ShowPropertyGridSync】它自己建窗口、自己收口成 bool，
    /// **没有地方放写盘失败的红字**，而 AppConfig.json 在程序目录、装在 Program Files 下时
    /// 这一步就是会失败。所以这里自己拿确定/取消与错误条，只把网格当正文控件。
    ///
    /// 【口径（与两个旧弹窗逐条对齐）】
    ///   ① 写软件级配置，不是 .vms；
    ///   ② 取消不留痕：只有确定才写盘；
    ///   ③ 打开时现读：<see cref="OnDialogOpened"/> 每次重建草稿，不依赖"容器每次新建 VM"；
    ///   ④ 改完立即生效：用它的地方每次现读配置（空闲计时器、两个落盘端、ScadaRuntimeHost），
    ///      所以这里不需要把新值推给谁；
    ///   ⑤ 审计的"事件"列保持**两个类别名**（运行窗口 / 系统参数）——历史流水的检索口径不能变，
    ///      一次确定同时改了两边就落两条。
    /// </summary>
    public class ScadaSystemSettingsViewModel : BindableBase, IDialogAware
    {
        /// <summary>Prism 对话框注册名（与视图类名同名，便于对照）</summary>
        public const string DialogName = "ScadaSystemSettingsView";

        /// <summary>打开时落在哪个页签的参数键（值取 <see cref="SystemSettingsDraft"/> 的 Group* 常量）</summary>
        public const string GroupParameterKey = "group";

        // 审计口径：这两个类别名是历史流水的检索词，合并弹窗后**不许**合并成一个
        private const string AuditEventWindow = "运行窗口";
        private const string AuditEventParameters = "系统参数";
        private const string AuditSubject = "AppConfig.json";
        private const string AuditActionWindow = "修改显示设置";
        private const string AuditActionParameters = "修改参数";

        private readonly AppSettingsService _settings;
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;
        private readonly Func<IReadOnlyList<ScadaMonitorOption>>? _monitorOptionsProvider;
        private readonly Action? _applyLogSettings;
        private readonly Action? _restartInboundServers;
        private readonly Action? _openLogFolder;

        private SystemSettingsDraft _draft;
        private string? _errorMessage;
        private string _initialGroup = SystemSettingsDraft.GroupRunWindow;

        /// <param name="audit">
        /// 操作审计落盘端。默认 null = 不记（单机调试 / 断言里直接 new 的路径）。
        /// <b>可选参数必须由宿主显式工厂注册</b>，否则编译能过、运行起来一条审计都没有且不报错。
        /// </param>
        /// <param name="actorProvider">"此刻登录着谁"（取不到时回落"未登录"，见 <see cref="ScadaAuditEntry"/>）</param>
        /// <param name="monitorOptionsProvider">
        /// 目标屏候选的取法。默认 null = 现取 <c>ScadaMonitors.All()</c>（真机路径）。
        /// 断言里注入一份固定清单，才能在不依赖"这台机器恰好接着几块屏"的前提下钉住
        /// "下拉文案 → 设备名"这条反查（单屏机器上真清单只有「跟随主屏」一项）。
        /// </param>
        /// <param name="applyLogSettings">
        /// 日志设置写盘后的收尾动作（宿主接到 <c>LogService.RequestReopen</c> + <c>CleanupNow</c>）：
        /// 目录要重开写入器、保留期要立刻清一次。null = 不动（断言里只看配置写没写对）。
        /// </param>
        /// <param name="restartInboundServers">
        /// 两个收图服务的重启动作（Host/Port 是绑定期决定的，不重开就换不了端口）。
        /// null = 不动（断言里不真起监听）。<b>抛异常 = 服务没起来</b>，由本类把原因写回弹窗。
        /// </param>
        /// <param name="openLogFolder">「打开日志目录」按钮的动作（null = 该行点了没反应，断言用）</param>
        public ScadaSystemSettingsViewModel(
            AppSettingsService settings,
            ScadaAuditWriter? audit = null,
            Func<string?>? actorProvider = null,
            Func<IReadOnlyList<ScadaMonitorOption>>? monitorOptionsProvider = null,
            Action? applyLogSettings = null,
            Action? restartInboundServers = null,
            Action? openLogFolder = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _audit = audit;
            _actorProvider = actorProvider;
            _monitorOptionsProvider = monitorOptionsProvider;
            _applyLogSettings = applyLogSettings;
            _restartInboundServers = restartInboundServers;
            _openLogFolder = openLogFolder;

            _draft = BuildDraft();
            _draft.PropertyChanged += OnDraftPropertyChanged;

            ConfirmCommand = new DelegateCommand(OnConfirm);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "系统设置";

        /// <summary>
        /// 编辑中的草稿。属性面板就地写它，点「确定」才由 <see cref="OnConfirm"/> 写回配置。
        /// 换新实例 = 整表重建（面板的 BindingObject 依赖属性会重新生成字段）。
        /// </summary>
        public SystemSettingsDraft Draft
        {
            get => _draft;
            private set
            {
                if (ReferenceEquals(_draft, value)) return;

                _draft.PropertyChanged -= OnDraftPropertyChanged;
                _draft = value;
                _draft.PropertyChanged += OnDraftPropertyChanged;

                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 打开时落在哪个页签（旧的两个菜单入口各自指向一页，见 ShellViewModel）。
        /// 绑定到属性面板的 <c>SelectedGroup</c>；找不到该页签时面板保持第一页，不报错。
        /// </summary>
        public string InitialGroup
        {
            get => _initialGroup;
            private set => SetProperty(ref _initialGroup, value);
        }

        /// <summary>
        /// 校验 / 写盘失败的原因（红字）。没有问题时为 <c>null</c>。
        /// <b>不弹全局通知</b>：通知飘一下就没了，而这句要留在弹窗上，
        /// 让用户看着它决定"重试还是先不管"。
        /// </summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    RaisePropertyChanged(nameof(HasError));
            }
        }

        /// <summary>只用来决定错误条显示与否（<c>string</c> 转可见性要额外一个转换器，不值当）</summary>
        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// 用户一动草稿就把红字清掉：上一轮的错误说的是"你刚才填的那个值"，
        /// 留在屏幕上会让人以为新填的也不对。
        /// </summary>
        private void OnDraftPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (ErrorMessage != null) ErrorMessage = null;
        }

        // ------------------------------------------------------------------
        //  草稿的铺开
        // ------------------------------------------------------------------

        /// <summary>
        /// 按当前配置铺一份新草稿。构造函数与 <see cref="OnDialogOpened"/> 共用一份——
        /// 两处各写一遍，早晚出现"新加一个字段只在其中一处灌"
        /// （表现为：首次打开是对的，再打开就退回旧值）。
        /// </summary>
        private SystemSettingsDraft BuildDraft()
        {
            var config = _settings.Current;
            var gallery = config.ImageGallery ?? new ImageGallerySettings();
            var http = config.HttpImageServer ?? new HttpImageServerSettings();
            var camera = config.NetworkCameraServer ?? new NetworkCameraServerSettings();
            var parallel = config.ParallelExecution ?? new ParallelExecutionSettings();
            var monitors = _monitorOptionsProvider?.Invoke() ?? BuildMonitorOptions();

            return new SystemSettingsDraft(monitors, _openLogFolder)
            {
                IndependentWindow = config.RunWindowMode == ScadaRunWindowMode.IndependentWindow,

                // 配置里那台显示器不在了（拔线 / 换口 / 别的机器）时归到「跟随主屏」——
                // 与运行时真正的回落结果一致，不会出现"看着选了副屏、实际跑在主屏"。
                MonitorLabel = DescribeMonitorLabel(monitors, config.RunWindowMonitor),

                IdleTimeoutText = config.IdleTimeoutMinutes.ToString(CultureInfo.InvariantCulture),
                AuditRetentionText = config.AuditRetentionDays.ToString(CultureInfo.InvariantCulture),
                AlarmRetentionText = config.AlarmHistoryRetentionDays.ToString(CultureInfo.InvariantCulture),
                MinPasswordLengthText = config.MinPasswordLength.ToString(CultureInfo.InvariantCulture),

                GalleryEnabled = gallery.Enabled,
                GalleryClearOnRun = gallery.RunStartMode == ImageGalleryRunStartMode.ClearAll,
                GalleryKeepHistory = gallery.KeepFrameHistory,
                GalleryKeepInjectedHistory = gallery.KeepInjectedHistory,
                GalleryShowThumbnailStrip = gallery.ShowThumbnailStrip,
                GalleryIncludeRealtime = gallery.IncludeRealtimePreviews,
                GalleryCollectAllPorts = gallery.CollectAllOutputPorts,
                GallerySaveOnAlarm = gallery.SaveFramesOnAlarm,
                GalleryTotalLimitText = gallery.TotalLimit.ToString(CultureInfo.InvariantCulture),
                GalleryPerFlowLimitText = gallery.PerFlowLimit.ToString(CultureInfo.InvariantCulture),
                AlarmFrameRetentionText = config.AlarmFrameRetentionDays.ToString(CultureInfo.InvariantCulture),

                LogRetentionText = config.LogRetentionDays.ToString(CultureInfo.InvariantCulture),
                LogDirectory = config.LogDirectory ?? "",
                LogFileLevel = config.LogFileLevel,
                LogPanelMaxRowsText = config.LogPanelMaxRows.ToString(CultureInfo.InvariantCulture),

                HttpServerEnabled = http.Enabled,
                HttpServerHost = http.Host ?? "",
                HttpServerPortText = http.Port.ToString(CultureInfo.InvariantCulture),
                HttpServerToken = http.Token ?? "",
                HttpServerTimeoutText = http.RequestTimeoutMs.ToString(CultureInfo.InvariantCulture),

                CameraServerEnabled = camera.Enabled,
                CameraServerHost = camera.Host ?? "",
                CameraServerPortText = camera.Port.ToString(CultureInfo.InvariantCulture),
                CameraServerToken = camera.Token ?? "",

                CommunicationStartupCheck = config.EnableCommunicationStartupCheck,

                ParallelFailFastByDefault = parallel.FailFastByDefault,
                ParallelForceSequential = parallel.ForceSequential,
            };
        }

        /// <summary>
        /// 拼下拉项。第一项固定是「跟随主屏」——它不是"选主屏"的别名：
        /// 跟随是"主屏换了我跟着换"，与"钉死在某块屏上"不是一回事。而且它是默认值：
        /// 升级后所有现场机器读到的都是空串，落屏行为与加这项之前逐字一致。
        /// </summary>
        private static IReadOnlyList<ScadaMonitorOption> BuildMonitorOptions()
        {
            var options = new List<ScadaMonitorOption>
            {
                new ScadaMonitorOption(
                    "",
                    "跟随主屏（默认）",
                    "不指定显示器：独立窗口落在主屏右下角，依附形态铺满主屏")
            };

            var all = ScadaMonitors.All();
            var primary = all.FirstOrDefault(m => m.IsPrimary);

            for (int i = 0; i < all.Count; i++)
            {
                var m = all[i];
                options.Add(new ScadaMonitorOption(
                    m.DeviceName,
                    // 物理分辨率（如 1920 × 1080）就是用户熟悉的那组数字，别拿 DIP 去换它
                    $"显示器 {i + 1}（{ScadaMonitors.DescribePosition(m, primary)}，{m.Bounds.Width:0} × {m.Bounds.Height:0}）",
                    m.DeviceName));
            }

            return options;
        }

        /// <summary>配置里的设备名 → 下拉文案；认不出就归「跟随主屏」（= 第一项）</summary>
        private static string DescribeMonitorLabel(IReadOnlyList<ScadaMonitorOption> monitors, string? deviceName)
        {
            var configured = deviceName ?? "";
            var match = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, configured, StringComparison.OrdinalIgnoreCase));

            return match?.Label ?? monitors[0].Label;
        }

        /// <summary>下拉文案 → 设备名。认不出（清单里没有这一项）时归空串 = 跟随主屏</summary>
        private string ResolveMonitorDeviceName(string? label)
        {
            var match = Draft.Monitors.FirstOrDefault(m =>
                string.Equals(m.Label, label ?? "", StringComparison.Ordinal));

            return match?.DeviceName ?? "";
        }

        // ------------------------------------------------------------------
        //  确定 / 取消
        // ------------------------------------------------------------------

        private void OnConfirm()
        {
            // 字段一起校验、一起报错：逐条弹窗式的"错一个改一个"在设置页上很烦人。
            // 短路求值顺带保证 error 里留下的是**第一条**出错的原因。
            if (!TryParseField(Draft.IdleTimeoutText, "空闲自动登出", out var idleMinutes, out var error)
                || !TryParseField(Draft.AuditRetentionText, "操作审计保留", out var auditDays, out error)
                || !TryParseField(Draft.AlarmRetentionText, "报警历史保留", out var alarmDays, out error)
                || !TryParseField(Draft.MinPasswordLengthText, "密码最小长度", out var minPassword, out error)
                || !TryParseField(Draft.GalleryTotalLimitText, "图集总张数上限", out var galleryTotal, out error)
                || !TryParseField(Draft.GalleryPerFlowLimitText, "图集单流程上限", out var galleryPerFlow, out error)
                || !TryParseField(Draft.AlarmFrameRetentionText, "报警图集保留天数", out var alarmFrameDays, out error)
                || !TryParseField(Draft.LogRetentionText, "日志保留天数", out var logRetention, out error)
                || !TryParseField(Draft.LogPanelMaxRowsText, "日志面板保留条数", out var logPanelRows, out error)
                || !TryParsePort(Draft.HttpServerPortText, "HTTP 收图服务端口", out var httpPort, out error)
                || !TryParseField(Draft.HttpServerTimeoutText, "HTTP 请求超时", out var httpTimeout, out error)
                || !TryParsePort(Draft.CameraServerPortText, "网络相机服务端口", out var cameraPort, out error))
            {
                ErrorMessage = error;
                return;
            }

            // 空令牌会让所有请求都验不过（Authenticate 要求 token.Length > 0 且相等），
            // "服务开着但谁都连不上"是最难查的一类故障——保存前就拦住。
            if (Draft.HttpServerEnabled && string.IsNullOrWhiteSpace(Draft.HttpServerToken))
            {
                ErrorMessage = "「HTTP 收图服务」的访问令牌不能留空：空令牌会让所有推图请求都验不过。";
                return;
            }
            if (Draft.CameraServerEnabled && string.IsNullOrWhiteSpace(Draft.CameraServerToken))
            {
                ErrorMessage = "「网络相机收图服务」的访问令牌不能留空：空令牌会让所有推帧请求都验不过。";
                return;
            }
            if (httpTimeout <= 0)
            {
                ErrorMessage = "「HTTP 请求超时」必须大于 0 毫秒。";
                return;
            }

            var config = _settings.Current;
            var gallery = config.ImageGallery ?? new ImageGallerySettings();
            var http = config.HttpImageServer ?? new HttpImageServerSettings();
            var camera = config.NetworkCameraServer ?? new NetworkCameraServerSettings();
            var parallel = config.ParallelExecution ?? new ParallelExecutionSettings();

            var newMode = Draft.IndependentWindow
                ? ScadaRunWindowMode.IndependentWindow
                : ScadaRunWindowMode.AttachedToMainWindow;
            var newMonitor = ResolveMonitorDeviceName(Draft.MonitorLabel);
            var newRunStart = Draft.GalleryClearOnRun
                ? ImageGalleryRunStartMode.ClearAll
                : ImageGalleryRunStartMode.ReplaceFlow;
            var newLogDirectory = (Draft.LogDirectory ?? string.Empty).Trim();
            var newPasswordLength = minPassword > 0 ? minPassword : AppConfigModel.DefaultMinPasswordLength;

            // 日志目录改了才试写：没改的时候不该因为"网络盘刚好断线"把无关的保存也拦下来。
            // 不试写的话，指到不可写目录的表现是"日志面板照常、文件一条没有"——最阴的一类静默故障。
            if (!string.Equals(newLogDirectory, config.LogDirectory ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && !TryProbeLogDirectory(newLogDirectory, out var logDirError))
            {
                ErrorMessage = logDirError;
                return;
            }

            // 先留旧值：审计"说明"列要写净效果（旧值 → 新值），落盘之后就读不到了。
            var oldMode = config.RunWindowMode;
            var oldMonitor = config.RunWindowMonitor ?? "";
            var oldIdle = config.IdleTimeoutMinutes;
            var oldAudit = config.AuditRetentionDays;
            var oldAlarm = config.AlarmHistoryRetentionDays;
            var oldMinPassword = config.MinPasswordLength;
            var oldGalleryTotal = gallery.TotalLimit;
            var oldGalleryPerFlow = gallery.PerFlowLimit;
            var oldGalleryEnabled = gallery.Enabled;
            var oldGalleryRunStart = gallery.RunStartMode;
            var oldGalleryCollectAll = gallery.CollectAllOutputPorts;
            var oldGalleryHistory = gallery.KeepFrameHistory;
            var oldGalleryInjectedHistory = gallery.KeepInjectedHistory;
            var oldGalleryStrip = gallery.ShowThumbnailStrip;
            var oldGalleryRealtime = gallery.IncludeRealtimePreviews;
            var oldGallerySaveOnAlarm = gallery.SaveFramesOnAlarm;
            var oldAlarmFrameDays = config.AlarmFrameRetentionDays;
            var oldLogRetention = config.LogRetentionDays;
            var oldLogDirectory = config.LogDirectory ?? "";
            var oldLogLevel = config.LogFileLevel;
            var oldLogPanelRows = config.LogPanelMaxRows;
            var oldCommCheck = config.EnableCommunicationStartupCheck;
            var oldHttp = (http.Enabled, http.Host ?? "", http.Port, http.Token ?? "", http.RequestTimeoutMs);
            var oldCamera = (camera.Enabled, camera.Host ?? "", camera.Port, camera.Token ?? "");
            var oldParallel = (parallel.FailFastByDefault, parallel.ForceSequential);

            // 只列**真变了的**项：一股脑列出来，事后就分不清"他改了哪一项"和"他只是点了一下确定"。
            // 两个域分开记：审计的"事件"列是历史流水检索词，合并弹窗不改这个口径。
            var windowChanges = new List<string>();
            if (newMode != oldMode)
                windowChanges.Add($"形态：{DescribeMode(oldMode)} → {DescribeMode(newMode)}");
            if (!string.Equals(newMonitor, oldMonitor, StringComparison.OrdinalIgnoreCase))
                windowChanges.Add($"目标屏：{DescribeMonitor(oldMonitor)} → {DescribeMonitor(newMonitor)}");

            var parameterChanges = new List<string>();
            if (idleMinutes != oldIdle) parameterChanges.Add($"空闲自动登出：{oldIdle} → {idleMinutes} 分钟");
            if (auditDays != oldAudit) parameterChanges.Add($"操作审计保留：{oldAudit} → {auditDays} 天");
            if (alarmDays != oldAlarm) parameterChanges.Add($"报警历史保留：{oldAlarm} → {alarmDays} 天");
            if (newPasswordLength != oldMinPassword)
                parameterChanges.Add($"密码最小长度：{oldMinPassword} → {newPasswordLength} 位");
            if (Draft.GalleryEnabled != oldGalleryEnabled)
                parameterChanges.Add($"图集采集：{(oldGalleryEnabled ? "启用" : "关闭")} → {(Draft.GalleryEnabled ? "启用" : "关闭")}");
            if (newRunStart != oldGalleryRunStart)
                parameterChanges.Add($"画布每轮开始：{RunStartText(oldGalleryRunStart)} → {RunStartText(newRunStart)}");
            if (Draft.GalleryCollectAllPorts != oldGalleryCollectAll)
                parameterChanges.Add($"兜底采集所有输出端口：{(oldGalleryCollectAll ? "是" : "否")} → {(Draft.GalleryCollectAllPorts ? "是" : "否")}");
            if (galleryTotal != oldGalleryTotal)
                parameterChanges.Add($"图集总张数上限：{oldGalleryTotal} → {galleryTotal} 张");
            if (galleryPerFlow != oldGalleryPerFlow)
                parameterChanges.Add($"图集单流程上限：{oldGalleryPerFlow} → {galleryPerFlow} 张");
            if (Draft.GalleryKeepHistory != oldGalleryHistory)
                parameterChanges.Add($"图集保留历史帧：{(oldGalleryHistory ? "是" : "否")} → {(Draft.GalleryKeepHistory ? "是" : "否")}");
            if (Draft.GalleryKeepInjectedHistory != oldGalleryInjectedHistory)
                parameterChanges.Add($"保留插件注入图的历史帧：{(oldGalleryInjectedHistory ? "是" : "否")} → {(Draft.GalleryKeepInjectedHistory ? "是" : "否")}");
            if (Draft.GalleryShowThumbnailStrip != oldGalleryStrip)
                parameterChanges.Add($"显示缩略图条：{(oldGalleryStrip ? "是" : "否")} → {(Draft.GalleryShowThumbnailStrip ? "是" : "否")}");
            if (Draft.GalleryIncludeRealtime != oldGalleryRealtime)
                parameterChanges.Add($"图集收录实时预览：{(oldGalleryRealtime ? "是" : "否")} → {(Draft.GalleryIncludeRealtime ? "是" : "否")}");
            if (Draft.GallerySaveOnAlarm != oldGallerySaveOnAlarm)
                parameterChanges.Add($"报警时留存图集：{(oldGallerySaveOnAlarm ? "是" : "否")} → {(Draft.GallerySaveOnAlarm ? "是" : "否")}");
            if (alarmFrameDays != oldAlarmFrameDays)
                parameterChanges.Add($"报警图集保留：{oldAlarmFrameDays} → {alarmFrameDays} 天");

            var logChanged = false;
            if (logRetention != oldLogRetention)
            {
                parameterChanges.Add($"日志保留：{oldLogRetention} → {logRetention} 天");
                logChanged = true;
            }
            if (!string.Equals(newLogDirectory, oldLogDirectory, StringComparison.OrdinalIgnoreCase))
            {
                parameterChanges.Add($"日志目录：{(oldLogDirectory.Length == 0 ? "（程序目录\\Logs）" : oldLogDirectory)}"
                                     + $" → {(newLogDirectory.Length == 0 ? "（程序目录\\Logs）" : newLogDirectory)}");
                logChanged = true;
            }
            if (Draft.LogFileLevel != oldLogLevel)
            {
                parameterChanges.Add($"日志落盘级别：{oldLogLevel} → {Draft.LogFileLevel}");
                logChanged = true;
            }
            if (logPanelRows != oldLogPanelRows)
                parameterChanges.Add($"日志面板保留条数：{oldLogPanelRows} → {logPanelRows} 条");

            var serviceChanged = false;
            if (Draft.HttpServerEnabled != oldHttp.Enabled)
            {
                parameterChanges.Add($"HTTP 收图服务：{(oldHttp.Enabled ? "启用" : "关闭")} → {(Draft.HttpServerEnabled ? "启用" : "关闭")}");
                serviceChanged = true;
            }
            if (!string.Equals(Draft.HttpServerHost?.Trim(), oldHttp.Item2, StringComparison.OrdinalIgnoreCase))
            {
                parameterChanges.Add($"HTTP 监听地址：{oldHttp.Item2} → {Draft.HttpServerHost?.Trim()}");
                serviceChanged = true;
            }
            if (httpPort != oldHttp.Port)
            {
                parameterChanges.Add($"HTTP 监听端口：{oldHttp.Port} → {httpPort}");
                serviceChanged = true;
            }
            // 令牌是凭据：审计只记"改过"，绝不把值写进流水
            if (!string.Equals(Draft.HttpServerToken, oldHttp.Item4, StringComparison.Ordinal))
            {
                parameterChanges.Add("HTTP 访问令牌：已修改");
                serviceChanged = true;
            }
            if (httpTimeout != oldHttp.Item5)
            {
                parameterChanges.Add($"HTTP 请求超时：{oldHttp.Item5} → {httpTimeout} ms");
                serviceChanged = true;
            }
            if (Draft.CameraServerEnabled != oldCamera.Enabled)
            {
                parameterChanges.Add($"网络相机服务：{(oldCamera.Enabled ? "启用" : "关闭")} → {(Draft.CameraServerEnabled ? "启用" : "关闭")}");
                serviceChanged = true;
            }
            if (!string.Equals(Draft.CameraServerHost?.Trim(), oldCamera.Item2, StringComparison.OrdinalIgnoreCase))
            {
                parameterChanges.Add($"网络相机监听地址：{oldCamera.Item2} → {Draft.CameraServerHost?.Trim()}");
                serviceChanged = true;
            }
            if (cameraPort != oldCamera.Port)
            {
                parameterChanges.Add($"网络相机监听端口：{oldCamera.Port} → {cameraPort}");
                serviceChanged = true;
            }
            if (!string.Equals(Draft.CameraServerToken, oldCamera.Item4, StringComparison.Ordinal))
            {
                parameterChanges.Add("网络相机访问令牌：已修改");
                serviceChanged = true;
            }
            if (Draft.CommunicationStartupCheck != oldCommCheck)
                parameterChanges.Add($"启动时通讯自检：{(oldCommCheck ? "开" : "关")} → {(Draft.CommunicationStartupCheck ? "开" : "关")}");

            if (Draft.ParallelFailFastByDefault != oldParallel.Item1)
                parameterChanges.Add($"并行失败聚合默认：{(oldParallel.Item1 ? "开" : "关")} → {(Draft.ParallelFailFastByDefault ? "开" : "关")}");
            if (Draft.ParallelForceSequential != oldParallel.Item2)
                parameterChanges.Add($"并行强制顺序总闸：{(oldParallel.Item2 ? "开" : "关")} → {(Draft.ParallelForceSequential ? "开" : "关")}");

            // 收容开关翻转必须走 ApplySettingsNow：它要**当场重整已收下帧的槽位**
            // （见 ImageCollectionService.ReslotInjectedFrames），只靠"下次采集自然生效"
            // 会让界面停在"旧键那条 + 新键这条"两张并存的中间态。
            var galleryChanged = parameterChanges.Count > 0 && (
                galleryTotal != oldGalleryTotal || galleryPerFlow != oldGalleryPerFlow
                || alarmFrameDays != oldAlarmFrameDays || Draft.GalleryEnabled != oldGalleryEnabled
                || Draft.GalleryKeepInjectedHistory != oldGalleryInjectedHistory);

            if (windowChanges.Count == 0 && parameterChanges.Count == 0)
            {
                // 一个值都没动就不写盘（配置内容与盘上那份逐字相同，重写一遍只是白动一次磁盘，
                // 而 AppConfig.json 在程序目录，某些部署下这一步本来就会失败——为一个没改的东西弹失败最没道理）。
                // 但仍然关窗：用户点的是「确定」，不是「取消」。
                Audit(AuditEventParameters, AuditActionParameters, ok: true,
                    detail: "各参数均未变，未写盘", error: null);
                RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
                return;
            }

            config.RunWindowMode = newMode;
            config.RunWindowMonitor = newMonitor;
            config.IdleTimeoutMinutes = idleMinutes;
            config.AuditRetentionDays = auditDays;
            config.AlarmHistoryRetentionDays = alarmDays;
            config.MinPasswordLength = newPasswordLength;
            config.AlarmFrameRetentionDays = alarmFrameDays;
            config.LogRetentionDays = logRetention;
            config.LogDirectory = newLogDirectory;
            config.LogFileLevel = Draft.LogFileLevel;
            config.LogPanelMaxRows = logPanelRows;
            config.EnableCommunicationStartupCheck = Draft.CommunicationStartupCheck;

            config.ImageGallery = gallery;
            gallery.Enabled = Draft.GalleryEnabled;
            gallery.RunStartMode = newRunStart;
            gallery.CollectAllOutputPorts = Draft.GalleryCollectAllPorts;
            gallery.TotalLimit = galleryTotal;
            gallery.PerFlowLimit = galleryPerFlow;
            gallery.KeepFrameHistory = Draft.GalleryKeepHistory;
            gallery.KeepInjectedHistory = Draft.GalleryKeepInjectedHistory;
            gallery.ShowThumbnailStrip = Draft.GalleryShowThumbnailStrip;
            gallery.IncludeRealtimePreviews = Draft.GalleryIncludeRealtime;
            gallery.SaveFramesOnAlarm = Draft.GallerySaveOnAlarm;

            config.HttpImageServer = http;
            http.Enabled = Draft.HttpServerEnabled;
            http.Host = (Draft.HttpServerHost ?? string.Empty).Trim();
            http.Port = httpPort;
            http.Token = Draft.HttpServerToken ?? string.Empty;
            http.RequestTimeoutMs = httpTimeout;

            config.NetworkCameraServer = camera;
            camera.Enabled = Draft.CameraServerEnabled;
            camera.Host = (Draft.CameraServerHost ?? string.Empty).Trim();
            camera.Port = cameraPort;
            camera.Token = Draft.CameraServerToken ?? string.Empty;

            config.ParallelExecution = parallel;
            parallel.FailFastByDefault = Draft.ParallelFailFastByDefault;
            parallel.ForceSequential = Draft.ParallelForceSequential;

            try
            {
                _settings.Save();   // 内部会 SyncParallelConfig：两个并行开关保存即生效
            }
            catch (Exception ex)
            {
                // 配置写在程序目录下：装在 Program Files 里、或以普通用户跑，这里就是会失败的。
                // 失败时**不关弹窗**——用户刚改的值还在界面上，关掉就等于让他重填一遍；
                // 并如实说清"没保存成功"，不能只说"失败"。
                ErrorMessage = $"保存失败，设置没有写入 AppConfig.json（重启后会回到原值）：{ex.Message}";

                // 成功与失败都记：只记成功的那一半，事后问"他到底动过没有"答案会是"没有"。
                if (windowChanges.Count > 0)
                    Audit(AuditEventWindow, AuditActionWindow, ok: false, detail: null, error: ErrorMessage);
                if (parameterChanges.Count > 0)
                    Audit(AuditEventParameters, AuditActionParameters, ok: false, detail: null, error: ErrorMessage);
                return;
            }

            // 落盘之后才做"生效动作"：配置已经是新的了，动作失败只影响"现在能不能用"，不影响"存了什么"。
            try
            {
                // 日志：目录要重开写入器、保留期要立刻清一次（否则"改完立即生效"是空话）
                if (logChanged) _applyLogSettings?.Invoke();

                // 张数上限 / 报警图集保留期可能被调小：立即按新值裁剪/清理一次
                if (galleryChanged) ImageCollectionService.Instance?.ApplySettingsNow();

                // 收图服务：Host/Port 是绑定期决定的，只能重开监听
                if (serviceChanged) _restartInboundServers?.Invoke();
            }
            catch (Exception ex)
            {
                // 配置已落盘，但服务没起来 —— 不关窗，让用户看着原因改端口后重试。
                ErrorMessage = $"设置已写入 AppConfig.json，但生效失败：{ex.Message}。"
                             + "若是收图服务，请检查端口是否被占用，改好后重新点一次「确定」。";

                if (windowChanges.Count > 0)
                    Audit(AuditEventWindow, AuditActionWindow, ok: true, detail: string.Join("；", windowChanges), error: null);
                Audit(AuditEventParameters, AuditActionParameters, ok: false, detail: null, error: ErrorMessage);
                return;
            }

            if (windowChanges.Count > 0)
                Audit(AuditEventWindow, AuditActionWindow, ok: true, detail: string.Join("；", windowChanges), error: null);
            if (parameterChanges.Count > 0)
                Audit(AuditEventParameters, AuditActionParameters, ok: true, detail: string.Join("；", parameterChanges), error: null);

            RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
        }


        /// <summary>审计"说明"列里怎么称呼一种形态。不认识的档位（高版本存下的）就写"未知"，不编一个中文名</summary>
        private static string DescribeMode(ScadaRunWindowMode mode)
            => mode switch
            {
                ScadaRunWindowMode.AttachedToMainWindow => "依附主窗口",
                ScadaRunWindowMode.IndependentWindow => "独立窗口",
                _ => "未知"
            };

        /// <summary>
        /// 审计"说明"列里怎么称呼一块屏。空串 = 跟随主屏（配置里就是这么存的），
        /// 其余直接写设备名——它才是与系统「显示设置」对得上的那个值，换成"显示器 2"反而要多绕一层。
        /// </summary>
        private static string DescribeMonitor(string deviceName)
            => string.IsNullOrEmpty(deviceName) ? "跟随主屏" : deviceName;

        /// <summary>图集留存模式的中文名（审计说明列用）</summary>
        private static string RunStartText(ImageGalleryRunStartMode mode)
            => mode == ImageGalleryRunStartMode.ClearAll ? "清空整张画布" : "覆盖本流程上一轮的图";

        /// <summary>
        /// 落一条审计。落盘端自己吞掉 IO 异常并报一次诊断（见 <see cref="ScadaAuditWriter"/>），
        /// 所以这里不 try、也不看返回值：审计写不成不该让"设置改没改成"这件事变样。
        /// </summary>
        private void Audit(string eventText, string actionText, bool ok, string? detail, string? error)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider?.Invoke(),
                AuditSubject,
                eventText,
                actionText,
                ok ? ScadaAuditOutcome.Success : ScadaAuditOutcome.Failed,
                ok ? detail : error));

        /// <summary>
        /// 解析一个整数字段。空串也走失败分支——"留空"不该被当成 0 悄悄变成"永不清理 / 不自动登出"，
        /// 那正好是最不该猜错的一个值。
        ///
        /// 口径是**不变区域性**：别让区域设置把 "1.000" 读成一千。
        /// </summary>
        private static bool TryParseField(string? text, string fieldName, out int value, out string? error)
        {
            error = null;
            var trimmed = (text ?? string.Empty).Trim();
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            error = string.IsNullOrEmpty(trimmed)
                ? $"「{fieldName}」不能留空，请填整数（0 表示不限制）。"
                : $"「{fieldName}」请填整数，当前填的是「{trimmed}」。";
            return false;
        }

        /// <summary>端口号：先按整数解析，再卡 1~65535（超范围要在保存前拦住，别等监听失败）</summary>
        private static bool TryParsePort(string? text, string fieldName, out int value, out string? error)
        {
            if (!TryParseField(text, fieldName, out value, out error)) return false;

            if (value < 1 || value > 65535)
            {
                error = $"「{fieldName}」请填 1~65535 之间的端口号，当前填的是「{value}」。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 日志目录试写：建目录 + 写一个探针文件再删掉。
        ///
        /// 为什么必须试：<c>LogService</c> 的文件通道在目录不可写时会静默降级（面板照常显示、
        /// 文件一条没有），现场根本看不出来——这正是"记录地址"这一项最容易埋的坑。
        /// </summary>
        private static bool TryProbeLogDirectory(string? configured, out string? error)
        {
            error = null;
            var dir = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "Logs")
                : configured!.Trim();

            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, $".vm_probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                error = $"「日志目录」不可写（{dir}）：{ex.Message}";
                return false;
            }
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>
        /// 打开时：先按参数定页签，再把配置里的当前值灌进草稿。
        ///
        /// 顺序不能反——<see cref="BuildDraft"/> 会重建草稿实例（属性面板整表重建），
        /// 页签是靠 <c>SelectedGroup</c> 依赖属性选中的，与草稿谁先谁后无关；
        /// 但先定页签能让"重建时保住选中页签"那条路径一次就落对。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters != null
                && parameters.TryGetValue<string>(GroupParameterKey, out var group)
                && !string.IsNullOrWhiteSpace(group))
            {
                InitialGroup = group;
            }

            Draft = BuildDraft();
            ErrorMessage = null;
        }

        #endregion
    }
}
