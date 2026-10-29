using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案级监控宿主：报警引擎 + 变量事件引擎 + 全画面节拍 + 报警历史落盘。
    ///
    /// 为什么从 <see cref="ScadaRuntimeHost"/> 拆出来
    /// ---------
    /// 这两套引擎原来只在「SCADA 编辑器 → 运行画面」时启动，关窗口就收场：
    /// 只跑流程、不开组态运行窗口时，报警不评估、变量事件不触发。它们本质上是
    /// <b>方案级监控</b>，不是“运行窗口的附属品”，所以生命周期改为跟着当前方案走：
    /// 方案加载即可用，窗口只负责把 <see cref="Context"/> 拿去显示。
    ///
    /// 与 <see cref="ScadaRuntimeHost"/> 的分工
    /// ---------
    /// · 本类：引擎、节拍、变量订阅、报警历史落盘、变量事件派发；
    /// · <see cref="ScadaRuntimeHost"/>：SCADA 运行会话/窗口、页面导航、图元事件接线；
    ///   它从本类拿 <see cref="Context"/>，不再自己 new 引擎。
    ///
    /// 线程
    /// ---------
    /// 报警/变量事件引擎的事件可能来自变量轮询线程；本类只用 <see cref="_uiDispatcher"/>
    /// 把“执行动作”切回 UI 线程，文件落盘走 <see cref="_persistGate"/> 串行。
    /// </summary>
    public sealed class ScadaMonitoringHost : IDisposable
    {
        private static readonly TimeSpan AlarmBeatInterval =
            TimeSpan.FromMilliseconds(ScadaBeatSource.DefaultIntervalMilliseconds);

        private readonly ILogService _log;
        private readonly IWorkspaceManager _workspace;
        private readonly IScadaValueSource _values;
        private readonly IScadaValueWriter? _valueWriter;
        private readonly IScadaAccessPolicy _accessPolicy;
        private readonly IScadaAxisSource? _axisSource;
        private readonly IScadaAxisCommandSink? _axisCommands;
        private readonly IScadaConfirmationService? _confirmations;
        private readonly IScadaActionDispatcher _dispatcher;
        private readonly AppSettingsService _appSettings;

        /// <summary>语言服务（v6）：本宿主的运行上下文也带一份，供子页/嵌入页解析 @键</summary>
        private readonly IScadaLanguageService? _languages;
        private readonly INotifyPropertyChanged? _workspaceNotifier;
        private readonly object _persistGate = new();

        private ScadaDocument? _document;
        private ScadaAlarmEngine? _alarms;
        private ScadaVariableEventEngine? _variableEvents;
        private ScadaScheduleEngine? _schedules;
        private ScadaAnnouncerService? _announcer;
        private ScadaBeatSource? _beat;
        private ScadaAlarmHistoryWriter? _writer;
        private ScadaAlarmHistoryStore? _store;
        private ScadaSoundAlarmService? _soundAlarm;
        private IReadOnlyList<ScadaAlarmRecord> _priorAlarms = Array.Empty<ScadaAlarmRecord>();
        private ScadaRuntimeContext? _context;
        private Dispatcher? _uiDispatcher;
        private bool _active;

        public ScadaMonitoringHost(
            ILogService log,
            IWorkspaceManager workspace,
            IScadaValueSource values,
            IScadaValueWriter? valueWriter,
            IScadaAccessPolicy accessPolicy,
            IScadaAxisSource? axisSource,
            IScadaAxisCommandSink? axisCommands,
            IScadaConfirmationService? confirmations,
            IScadaActionDispatcher dispatcher,
            AppSettingsService appSettings,
            IScadaLanguageService? languages = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _values = values ?? throw new ArgumentNullException(nameof(values));
            _valueWriter = valueWriter;
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _axisSource = axisSource;
            _axisCommands = axisCommands;
            _confirmations = confirmations;
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
            _languages = languages;

            _workspaceNotifier = workspace as INotifyPropertyChanged;
            if (_workspaceNotifier != null)
                _workspaceNotifier.PropertyChanged += OnWorkspacePropertyChanged;

            // 应用退出时把节拍与变量订阅收干净；宿主是单例，没有别的生命周期钩子可挂。
            if (Application.Current != null)
                Application.Current.Exit += OnApplicationExit;

            // 方案可能在本宿主构造前就已经打开：下一拍 UI 消息里补一次 EnsureStarted。
            var initial = _workspace.CurrentSolution?.Scada;
            if (initial != null)
            {
                var dispatcherUi = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                dispatcherUi.BeginInvoke(new Action(() => EnsureStarted(initial)), DispatcherPriority.Background);
            }
        }

        /// <summary>当前监控上下文；没启动/已收场时为 null</summary>
        public ScadaRuntimeContext? Context => _context;

        /// <summary>当前监控的方案文档；没启动时为 null</summary>
        public ScadaDocument? Document => _document;

        /// <summary>是否正在监控（引擎已挂、节拍在跑）</summary>
        public bool IsActive => _active;

        /// <summary>
        /// 报警声音服务；没启动/已收场时为 null。
        ///
        /// 为什么挂在方案级监控而不是运行窗口：报警在"只跑流程、不开运行窗口"时照样评估
        /// （见本类注释），声音也该跟着同一份事实走——否则操作员在编辑器里看着报警条变红，
        /// 却听不到任何提示，得先点"运行"才知道有报警。
        /// </summary>
        public ScadaSoundAlarmService? SoundAlarm => _soundAlarm;

        /// <summary>
        /// 确保当前文档正在被监控。同一文档重复调用是空操作；换文档会先停旧的再起新的。
        /// 只能从 UI 线程调用（节拍源要 Dispatcher）。
        /// </summary>
        public void EnsureStarted(ScadaDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            if (_active && ReferenceEquals(_document, document))
                return;

            Stop();

            _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            _document = document;

            // 方案级监控默认无人值守：变量事件里的“执行前确认”不许弹模态框。
            // 运行窗口打开时由 ScadaRuntimeHost 置回 true（那时才有人能点）。
            ScadaConfirmationService.InteractiveAllowed = false;

            var alarmDirectory = ScadaAlarmHistoryStore.ResolveDirectory(_workspace.CurrentSolution);
            _store = new ScadaAlarmHistoryStore(alarmDirectory, OnDiagnostic);
            _priorAlarms = _store.Load();
            if (_priorAlarms.Count > 0)
                _log.Info($"[报警] 已从方案目录恢复历史 {_priorAlarms.Count} 条：{alarmDirectory}");

            _writer = new ScadaAlarmHistoryWriter(
                alarmDirectory,
                OnDiagnostic,
                retentionProvider: () => _appSettings.Current.AlarmHistoryRetentionDays);

            _alarms = new ScadaAlarmEngine(document, _values);
            _alarms.AlarmRaised += OnAlarmRaised;
            _alarms.AlarmChanged += OnAlarmChanged;

            // 声音服务必须在 Attach() 之前建：Attach 会对每条定义立即初判一次，
            // "启动那一刻早就越限"的报警正是最该响的那一类，晚一步就漏掉了。
            _soundAlarm = new ScadaSoundAlarmService(_alarms, new SystemSoundPlayer(), OnDiagnostic);
            _soundAlarm.Volume = Math.Clamp(
                _appSettings.Current.AlarmSoundVolume,
                0,
                100);
            _soundAlarm.MuteMinutes = Math.Clamp(
                _appSettings.Current.AlarmSoundMuteMinutes,
                1,
                1440);
            _soundAlarm.EnabledChanged += OnSoundEnabledChanged;
            _soundAlarm.Enabled = _appSettings.Current.AlarmSoundEnabled;

            _alarms.Attach();

            // 语音播报：与提示音共用一个"让路"开关——说话期间把提示音压住，
            // 否则人声和滴滴声叠在一起，现场一个字也听不清。
            _announcer = new ScadaAnnouncerService(
                new SapiSpeechEngine(),
                suspended =>
                {
                    var tone = _soundAlarm;
                    if (tone != null)
                        tone.Suspended = suspended;
                },
                _log);

            // 组态动作表的「语音播报」与报警播报共用同一个队列：两套队列会抢着说话，
            // 而且抢占/去重/压住提示音这三条规则只该有一份。会话结束时摘掉（见 Stop）。
            _dispatcher.Announcer = _announcer;

            ApplySpeechSettings();

            // 顶栏「静音 N 分钟」要连播报一起停：声音服务是那个静音状态的唯一出处，
            // 播报器只是跟着它的状态翻自己的总开关（紧急级报警仍不受静音约束，由服务侧决定）。
            _soundAlarm.StateChanged += OnSoundStateChanged;

            _variableEvents = new ScadaVariableEventEngine(document, _values);
            _variableEvents.VariableEventRaised += OnVariableEventRaised;
            _variableEvents.Attach();

            // 计划表：与报警/变量事件同一轮生命周期（方案级），挂在同一个节拍上。
            // 放在监控宿主而不是运行窗口：计划表是"方案在跑就该生效"，与有没有人开着运行窗口无关。
            _schedules = new ScadaScheduleEngine(document, _values, _dispatcher, _log);
            _schedules.Attach();

            _beat = new ScadaBeatSource(_uiDispatcher, AlarmBeatInterval, OnDiagnostic);
            _beat.Beat += OnBeat;
            _beat.Start();

            // 历史数据源：<方案根>\History 下的 CSV 账本（与「CSV 记录」插件的输出目录同一个约定）。
            // 方案没保存过时目录为 null，数据表格会显示"未接入历史数据源"而不是空表。
            var historyDirectory = SolutionPaths.GetHistoryDirectory(_workspace.CurrentSolution?.SolutionFilePath);
            var history = new CsvFolderHistorySource(historyDirectory);
            var reports = new CsvReportSource(historyDirectory);

            _context = new ScadaRuntimeContext(
                _alarms,
                _beat,
                _valueWriter,
                _accessPolicy,
                _axisSource,
                _axisCommands,
                _confirmations,
                _values,
                _priorAlarms,
                history,
            document.StateLists,
            reports: reports,
            reportExporter: new ScadaReportExporter(),
            languages: _languages);

            _active = true;

            if (_alarms.SlotCount > 0)
                _log.Info($"[报警] 引擎已挂载：定义 {_alarms.SlotCount} 条 / 订阅变量 {_alarms.SubscribedCount} 个 / 节拍 {AlarmBeatInterval.TotalMilliseconds:0}ms");
            if (_variableEvents.SlotCount > 0)
                _log.Info($"[变量事件] 引擎已挂载：记录 {_variableEvents.SlotCount} 条 / 订阅变量 {_variableEvents.SubscribedCount} 个");
        }

        /// <summary>停止监控并落一次报警快照。可重复调用。</summary>
        public void Stop()
        {
            if (!_active && _document == null)
                return;

            _active = false;

            // 语音先收：它攥着"压住提示音"的开关，晚于提示音收场会让开关停在 true 上。
            // 摘掉分发器上的引用要排在 Dispose 之前：否则会话收场后按按钮仍会往一个已停的队列里塞话。
            _dispatcher.Announcer = null;
            _announcer?.Dispose();
            _announcer = null;

            // 先退声音：它的节拍与引擎订阅都该在引擎 Detach 之前收干净，
            // 反过来的话最后一拍可能对着已经收场的引擎取值。
            if (_soundAlarm != null)
            {
                _soundAlarm.EnabledChanged -= OnSoundEnabledChanged;
                _soundAlarm.StateChanged -= OnSoundStateChanged;
                _soundAlarm.Dispose();
                _soundAlarm = null;
            }

            if (_beat != null)
            {
                _beat.Stop();
                _beat.Beat -= OnBeat;
                _beat = null;
            }

            if (_alarms != null)
            {
                _alarms.AlarmRaised -= OnAlarmRaised;
                _alarms.AlarmChanged -= OnAlarmChanged;

                lock (_persistGate)
                {
                    var recent = _alarms.RecentRecords(ScadaAlarmHistoryStore.MaxRecords);
                    if (recent.Count > 0)
                        _store?.Save(ScadaAlarmHistoryStore.Merge(_priorAlarms, recent));
                }

                _alarms.Detach();
                _alarms = null;
            }

            if (_variableEvents != null)
            {
                _variableEvents.VariableEventRaised -= OnVariableEventRaised;
                _variableEvents.Detach();
                _variableEvents = null;
            }

            _schedules?.Detach();
            _schedules = null;

            _writer = null;
            _store = null;
            _priorAlarms = Array.Empty<ScadaAlarmRecord>();
            _context = null;
            _document = null;
            _uiDispatcher = null;
        }

        private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IReadOnlyWorkspaceContext.CurrentSolution))
                return;

            var document = _workspace.CurrentSolution?.Scada;
            if (document == null)
            {
                Stop();
                return;
            }

            EnsureStarted(document);
        }

        private void OnApplicationExit(object? sender, ExitEventArgs e) => Stop();

        private void OnBeat(TimeSpan elapsed)
        {
            _alarms?.Tick();
            _variableEvents?.Tick();
            // 计划表自己节流到 1 秒（见 ScadaScheduleEngine.Tick），不必在这里再降频
            _schedules?.Tick(DateTime.Now);
        }

        private void OnAlarmRaised(ScadaAlarmRecord record)
        {
            var text = $"[报警] {record.Severity.DisplayName()}｜{record.Name}：{record.Message}"
                     + $"（{record.ConditionText}，触发值 {record.TriggerValue ?? "—"}）";
            if (record.Severity >= ScadaAlarmSeverity.Critical)
                _log.Error(text);
            else
                _log.Warn(text);

            try { ImageCollectionService.Instance?.SaveSnapshot("报警_" + record.Name); }
            catch (Exception ex) { _log.Warn($"[报警留存] 图像集留存失败：{ex.Message}"); }

            // 语音播报：只在报警<b>产生</b>时念一次（不跟 AlarmChanged，否则恢复/确认也会念）。
            // 去重键用报警 Id + 激活时间：同一条报警不会因为状态迁移被反复念。
            var spoken = string.IsNullOrWhiteSpace(record.Message)
                ? $"{record.Severity.DisplayName()}，{record.Name}"
                : $"{record.Severity.DisplayName()}，{record.Name}，{record.Message}";

            _announcer?.Announce(spoken, record.Severity, record.AlarmId + "@" + record.ActivatedAtUtc.Ticks);
        }

        /// <summary>报警态迁移统一落盘：CSV 一行一次迁移，JSON 全量快照（合并 prior）。</summary>
        private void OnAlarmChanged(ScadaAlarmRecord record)
        {
            lock (_persistGate)
            {
                _writer?.Append(record);
                var recent = _alarms?.RecentRecords(ScadaAlarmHistoryStore.MaxRecords);
                if (recent != null)
                    _store?.Save(ScadaAlarmHistoryStore.Merge(_priorAlarms, recent));
            }
        }

        private void OnVariableEventRaised(ScadaVariableEvent record, ScadaEventHook hook)
        {
            var dispatcher = _uiDispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_active || _dispatcher == null)
                        return;

                    _dispatcher.Dispatch(hook, record.VariableName, _accessPolicy.CurrentUserName);
                }), DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                _log.Warn($"[变量事件] 动作投递失败：{ex.Message}");
            }
        }

        private void OnDiagnostic(ScadaDiagnosticLevel level, string message)
        {
            if (level >= ScadaDiagnosticLevel.Error)
                _log.Error(message);
            else
                _log.Warn(message);
        }

        /// <summary>
        /// 运行期开关声音：把偏好落盘，下次启动沿用。
        /// 只订阅 <see cref="ScadaSoundAlarmService.EnabledChanged"/> 而不订阅 <c>StateChanged</c>：
        /// 后者每 2 秒就会因为重复音触发一次，拿它落盘等于每 2 秒写一次配置文件。
        /// </summary>
        private void OnSoundEnabledChanged(object? sender, EventArgs e)
        {
            var service = _soundAlarm;
            if (service == null || _appSettings.Current.AlarmSoundEnabled == service.Enabled)
                return;

            _appSettings.Current.AlarmSoundEnabled = service.Enabled;

            try
            {
                _appSettings.Save();
            }
            catch (Exception ex)
            {
                // 存不下不该让"开关声音"这个动作失败：本次运行照常按新状态发声。
                _log.Warn($"[报警] 保存声音开关失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 把「系统设置 → 语音播报」那一组配置应用到播报器上（会话启动时一次）。
        /// 音色填了本机没有的名字时，播报器内部的 SelectVoice 会静默忽略——
        /// 这里补一条 Warn，否则用户会以为"改了音色没生效"是软件的锅。
        /// </summary>
        private void ApplySpeechSettings()
        {
            var announcer = _announcer;
            if (announcer == null)
                return;

            var settings = _appSettings.Current;
            announcer.Rate = Math.Clamp(settings.SpeechRate, -10, 10);
            announcer.MinSeverity = ParseSpeechSeverity(settings.SpeechMinSeverity);

            var voice = (settings.SpeechVoice ?? string.Empty).Trim();
            if (voice.Length > 0)
            {
                if (announcer.Voices.Any(name => string.Equals(name, voice, StringComparison.OrdinalIgnoreCase)))
                    announcer.VoiceName = voice;
                else
                    _log.Warn($"[语音] 配置的音色「{voice}」本机没有安装，沿用系统默认语音；"
                              + $"可用：{(announcer.Voices.Count == 0 ? "（无）" : string.Join("、", announcer.Voices))}");
            }

            ApplySpeechMuteState();
        }

        /// <summary>总开关 = 配置里打开 且 顶栏没有在静音</summary>
        private void ApplySpeechMuteState()
        {
            var announcer = _announcer;
            if (announcer == null)
                return;

            announcer.Enabled = _appSettings.Current.SpeechEnabled && _soundAlarm?.IsMuted != true;
        }

        private void OnSoundStateChanged(object? sender, EventArgs e) => ApplySpeechMuteState();

        private static ScadaAlarmSeverity ParseSpeechSeverity(string? text) => text switch
        {
            "Info" => ScadaAlarmSeverity.Info,
            "Critical" => ScadaAlarmSeverity.Critical,
            _ => ScadaAlarmSeverity.Warning,
        };

        public void Dispose()
        {            if (_workspaceNotifier != null)
                _workspaceNotifier.PropertyChanged -= OnWorkspacePropertyChanged;
            if (Application.Current != null)
                Application.Current.Exit -= OnApplicationExit;
            Stop();
        }
    }
}
