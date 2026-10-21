using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
        private readonly INotifyPropertyChanged? _workspaceNotifier;
        private readonly object _persistGate = new();

        private ScadaDocument? _document;
        private ScadaAlarmEngine? _alarms;
        private ScadaVariableEventEngine? _variableEvents;
        private ScadaBeatSource? _beat;
        private ScadaAlarmHistoryWriter? _writer;
        private ScadaAlarmHistoryStore? _store;
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
            AppSettingsService appSettings)
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
            _alarms.Attach();

            _variableEvents = new ScadaVariableEventEngine(document, _values);
            _variableEvents.VariableEventRaised += OnVariableEventRaised;
            _variableEvents.Attach();

            _beat = new ScadaBeatSource(_uiDispatcher, AlarmBeatInterval, OnDiagnostic);
            _beat.Beat += OnBeat;
            _beat.Start();

            _context = new ScadaRuntimeContext(
                _alarms,
                _beat,
                _valueWriter,
                _accessPolicy,
                _axisSource,
                _axisCommands,
                _confirmations,
                _values,
                _priorAlarms);

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

        public void Dispose()
        {
            if (_workspaceNotifier != null)
                _workspaceNotifier.PropertyChanged -= OnWorkspacePropertyChanged;
            if (Application.Current != null)
                Application.Current.Exit -= OnApplicationExit;
            Stop();
        }
    }
}
