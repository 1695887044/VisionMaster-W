using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using Core.Interfaces;
using Core.Interfaces.Io;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Services.Io;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>设备行（值语义快照的 UI 投影）。</summary>
    public sealed class IoDeviceHealthRow : BindableBase
    {
        private IoDeviceHealth _snapshot;

        public IoDeviceHealthRow(IoDeviceHealth snapshot)
        {
            _snapshot = snapshot;
        }

        public string DeviceId => _snapshot.DeviceId;
        public string DisplayName => _snapshot.DisplayName;
        public string DriverId => _snapshot.DriverId;
        public string BusId => _snapshot.BusId;

        public string StateText => _snapshot.State switch
        {
            IoDeviceState.Closed => "未连接",
            IoDeviceState.Connecting => "连接中",
            IoDeviceState.Online => "在线",
            IoDeviceState.Degraded => "降级",
            IoDeviceState.Reconnecting => "重连中",
            IoDeviceState.Faulted => "故障",
            IoDeviceState.Disabled => "禁用",
            _ => _snapshot.State.ToString()
        };

        public string StateDetail => _snapshot.StateDetail;
        public string LastSuccessText => _snapshot.LastSuccessTime == default
            ? "从未成功"
            : _snapshot.LastSuccessTime.ToString("HH:mm:ss");

        public string CountersText =>
            $"写 {_snapshot.WriteCount} / 拒 {_snapshot.RejectedWriteCount} / 故障 {_snapshot.FaultCount}";

        public string IsolatedText => _snapshot.IsolatedPointCount == 0
            ? string.Empty
            : $"{_snapshot.IsolatedPointCount} 个（{string.Join("、", _snapshot.IsolatedPoints)}）";

        /// <summary>状态色："在线"绿、"重连/降级"黄、"故障"红，其余灰。</summary>
        public string StateLevel => _snapshot.State switch
        {
            IoDeviceState.Online => "ok",
            IoDeviceState.Reconnecting or IoDeviceState.Degraded or IoDeviceState.Connecting => "warn",
            IoDeviceState.Faulted => "error",
            _ => "idle"
        };

        /// <summary>原位更新（1s 刷新时不清列表，避免选中态与滚动位置被重置）。</summary>
        public void Update(IoDeviceHealth snapshot)
        {
            _snapshot = snapshot;
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(DriverId));
            RaisePropertyChanged(nameof(BusId));
            RaisePropertyChanged(nameof(StateText));
            RaisePropertyChanged(nameof(StateDetail));
            RaisePropertyChanged(nameof(LastSuccessText));
            RaisePropertyChanged(nameof(CountersText));
            RaisePropertyChanged(nameof(IsolatedText));
            RaisePropertyChanged(nameof(StateLevel));
        }
    }

    /// <summary>总线行。</summary>
    public sealed class IoBusHealthRow : BindableBase
    {
        private IoBusHealth _snapshot;

        public IoBusHealthRow(IoBusHealth snapshot) => _snapshot = snapshot;

        public string BusId => _snapshot.BusId;
        public string DisplayName => _snapshot.DisplayName;
        public string Transport => _snapshot.Transport;
        public string DeviceText => $"{_snapshot.OnlineCount}/{_snapshot.DeviceCount} 在线";
        public string FaultText => _snapshot.FaultedCount == 0 ? "" : $"{_snapshot.FaultedCount} 故障";

        /// <summary>
        /// 实测轮询周期；没有样本时显示"—"（不要显示 0ms，那会被当成"极快"）。
        /// <para>模拟设备/内网 PLC 的单轮可能只有几十微秒，按 F1 会显示成 "0.0 ms"
        /// ——那和"没样本"看起来一样。所以小于 0.1ms 时明确显示 "&lt;0.1 ms"。</para>
        /// </summary>
        public string PollText => _snapshot.AveragePollMs <= 0
            ? "—"
            : _snapshot.AveragePollMs < 0.1
                ? "<0.1 ms"
                : $"{_snapshot.AveragePollMs:F1} ms";

        public void Update(IoBusHealth snapshot)
        {
            _snapshot = snapshot;
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(Transport));
            RaisePropertyChanged(nameof(DeviceText));
            RaisePropertyChanged(nameof(FaultText));
            RaisePropertyChanged(nameof(PollText));
        }
    }

    /// <summary>点位行。</summary>
    public sealed class IoPointHealthRow : BindableBase
    {
        private IoPointHealth _snapshot;

        public IoPointHealthRow(IoPointHealth snapshot) => _snapshot = snapshot;

        public string DeviceId => _snapshot.DeviceId;
        public string PointId => _snapshot.PointId;
        public string DisplayName => _snapshot.DisplayName;
        public string KindText => _snapshot.Kind switch
        {
            IoPointKind.DigitalInput => "DI",
            IoPointKind.DigitalOutput => "DO",
            IoPointKind.AnalogInput => "AI",
            IoPointKind.AnalogOutput => "AO",
            _ => _snapshot.Kind.ToString()
        };

        public string QualityText => _snapshot.Quality switch
        {
            IoQuality.Good => "良好",
            IoQuality.Uncertain => "不确定",
            IoQuality.Bad => "故障",
            IoQuality.Disabled => "禁用",
            _ => _snapshot.Quality.ToString()
        };

        public string QualityLevel => _snapshot.Quality switch
        {
            IoQuality.Good => "ok",
            IoQuality.Uncertain => "warn",
            IoQuality.Bad => "error",
            _ => "idle"
        };

        public string EnabledText => _snapshot.Enabled ? "启用" : "禁用";

        public string ValueText
        {
            get
            {
                if (_snapshot.Value == null) return "—";
                if (_snapshot.Value is bool b) return b ? "1" : "0";
                if (_snapshot.Value is double d) return d.ToString("F3");
                return _snapshot.Value.ToString() ?? "—";
            }
        }

        public string UpdatedText => _snapshot.LastUpdateTime == default
            ? "—"
            : _snapshot.LastUpdateTime.ToString("HH:mm:ss");

        public void Update(IoPointHealth snapshot)
        {
            _snapshot = snapshot;
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(QualityText));
            RaisePropertyChanged(nameof(QualityLevel));
            RaisePropertyChanged(nameof(EnabledText));
            RaisePropertyChanged(nameof(ValueText));
            RaisePropertyChanged(nameof(UpdatedText));
        }
    }

    /// <summary>
    /// IO 写入审计行（一次输出写入 = 一条）。
    ///
    /// <para>只做"展示投影"，不持有可写状态：审计条目是不可变的流水，
    /// 界面能做的只有筛选与查看——改审计等于销毁证据，这里刻意不给任何编辑入口。</para>
    /// </summary>
    public sealed class IoAuditRow
    {
        public IoAuditRow(VisionMaster.Core.AuditLogEntry entry)
        {
            Timestamp = entry.Timestamp;
            Succeeded = entry.Result;
            TimeText = entry.Timestamp.ToString("HH:mm:ss.fff");
            ResultText = entry.Result ? "成功" : "失败";
            Operator = string.IsNullOrWhiteSpace(entry.UserName) ? entry.UserId : entry.UserName;
            Details = entry.Details ?? string.Empty;
            PointText = ExtractPoint(Details);
        }

        public DateTime Timestamp { get; }
        public bool Succeeded { get; }
        public string TimeText { get; }
        public string ResultText { get; }
        public string Operator { get; }
        public string Details { get; }
        public string PointText { get; }

        /// <summary>结果灯（与设备/点位页签同一套 ok/error 样式）。</summary>
        public string ResultLevel => Succeeded ? "ok" : "error";

        /// <summary>从"点位=DEV.PT；旧值=…；…"里取出点位；取不到就留空（详情列仍有原文）。</summary>
        private static string ExtractPoint(string details)
        {
            const string marker = "点位=";
            int start = details.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;

            start += marker.Length;
            int end = details.IndexOf('；', start);
            return end < 0 ? details.Substring(start) : details.Substring(start, end - start);
        }
    }

    /// <summary>
    /// IO 健康面板：总线 / 设备 / 点位三层只读诊断，1s 自动刷新。
    ///
    /// 三条口径：
    /// 1) 数据来自 <see cref="IoHealthMonitor.Capture"/> 的**不可变快照**，UI 与 Worker 线程无共享；
    /// 2) 原位更新行对象，不清空重建（清空会每秒重置选中态与滚动位置）；
    /// 3) 刷新异常停表并把原因写进摘要，不弹泡、不重复刷——与扫描组编辑器的诊断节拍同一处置。
    /// </summary>
    public sealed class IoHealthViewModel : BindableBase, IDialogAware
    {
        private readonly IoHealthMonitor _monitor;
        private readonly DispatcherTimer _timer;

        private bool _hasData;
        private string _summary = "正在采集…";
        private string _hint = string.Empty;
        private int _selectedTab;

        public IoHealthViewModel(IoHealthMonitor monitor)
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, _) => SafeRefresh();

            RefreshCommand = new DelegateCommand(SafeRefresh);
            CloseCommand = new DelegateCommand(() =>
            {
                _timer.Stop();
                RequestClose.Invoke();
            });
        }

        public string Title => "IO 健康";

        public ObservableCollection<IoBusHealthRow> Buses { get; } = new();
        public ObservableCollection<IoDeviceHealthRow> Devices { get; } = new();
        public ObservableCollection<IoPointHealthRow> Points { get; } = new();

        // ------------------------------------------------------------------
        //  写入审计页签
        // ------------------------------------------------------------------

        /// <summary>IO 写入审计的操作名前缀（成功/失败两种文案共用；见 IoOutputVariableModel.RecordAudit）。</summary>
        public const string IoWriteAuditPrefix = "IO 输出写入";

        /// <summary>审计页签最多展示多少条（更早的去 audit_logs 文件里查）。</summary>
        public const int AuditDisplayLimit = 200;

        public ObservableCollection<IoAuditRow> AuditRows { get; } = new();

        private string _auditQuery = string.Empty;
        /// <summary>审计搜索词（匹配详情 / 操作者，大小写不敏感）。</summary>
        public string AuditQuery
        {
            get => _auditQuery;
            set
            {
                if (SetProperty(ref _auditQuery, value))
                    RefreshAudit();
            }
        }

        private bool _auditFailuresOnly;
        /// <summary>只看失败写入（排查"写下去没生效"时第一件事）。</summary>
        public bool AuditFailuresOnly
        {
            get => _auditFailuresOnly;
            set
            {
                if (SetProperty(ref _auditFailuresOnly, value))
                    RefreshAudit();
            }
        }

        private string _auditSummary = "暂无写入记录";
        public string AuditSummary
        {
            get => _auditSummary;
            private set => SetProperty(ref _auditSummary, value);
        }

        private string _lastAuditStamp = string.Empty;

        /// <summary>
        /// 从审计流水里挑出 IO 写入条目（纯函数，可脱离 WPF 断言）。
        ///
        /// <para>口径：</para>
        /// <list type="bullet">
        /// <item>只认 <c>OperationName</c> 以「IO 输出写入」开头——成功与失败两条文案共用前缀，
        /// 不用 <c>OperationType</c> 是因为它太宽（DeviceControl 还包含运动/相机等操作）；</item>
        /// <item>输入按「新→旧」给（<c>AuditLogger.RecentEntries</c> 的顺序），输出保持该顺序；</item>
        /// <item>搜索匹配详情 / 用户名 / 用户 ID，覆盖"查某个点位"与"查某个人"两种意图。</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<VisionMaster.Core.AuditLogEntry> SelectIoWriteEntries(
            IEnumerable<VisionMaster.Core.AuditLogEntry>? entries,
            string? query,
            bool failuresOnly,
            int max = AuditDisplayLimit)
        {
            if (entries == null) return Array.Empty<VisionMaster.Core.AuditLogEntry>();

            string q = query?.Trim() ?? string.Empty;
            var result = new List<VisionMaster.Core.AuditLogEntry>();

            foreach (var entry in entries)
            {
                if (entry == null) continue;
                if (entry.OperationName == null ||
                    !entry.OperationName.StartsWith(IoWriteAuditPrefix, StringComparison.Ordinal))
                    continue;
                if (failuresOnly && entry.Result) continue;
                if (q.Length > 0 && !Matches(entry, q)) continue;

                result.Add(entry);
                if (result.Count >= Math.Max(1, max)) break;
            }

            return result;
        }

        private static bool Matches(VisionMaster.Core.AuditLogEntry entry, string query)
            => Contains(entry.Details, query)
               || Contains(entry.UserName, query)
               || Contains(entry.UserId, query);

        private static bool Contains(string? value, string query)
            => !string.IsNullOrEmpty(value)
               && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// 刷新审计页签。
        /// <para>只在"最新一条或筛选条件变了"时重建：审计是只追加的流水，
        /// 每秒无条件 Clear+Add 会把用户正在看的那一行位置每秒重置。</para>
        /// </summary>
        private void RefreshAudit()
        {
            var entries = SelectIoWriteEntries(
                VisionMaster.Core.AuditLogger.RecentEntries, AuditQuery, AuditFailuresOnly);

            AuditSummary = entries.Count == 0
                ? "暂无写入记录"
                : $"最近 {entries.Count} 条" + (AuditFailuresOnly ? "（仅失败）" : string.Empty);

            string stamp = entries.Count == 0
                ? $"{AuditQuery}|{AuditFailuresOnly}|empty"
                : $"{AuditQuery}|{AuditFailuresOnly}|{entries.Count}|{entries[0].Timestamp.Ticks}|{entries[0].Details}";

            if (stamp == _lastAuditStamp) return;
            _lastAuditStamp = stamp;

            AuditRows.Clear();
            foreach (var entry in entries)
                AuditRows.Add(new IoAuditRow(entry));
        }

        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand CloseCommand { get; }

        /// <summary>0=设备 1=总线 2=点位 3=写入审计（绑定 TabControl.SelectedIndex）。</summary>
        public int SelectedTab
        {
            get => _selectedTab;
            set => SetProperty(ref _selectedTab, value);
        }

        public bool HasData
        {
            get => _hasData;
            private set => SetProperty(ref _hasData, value);
        }

        /// <summary>一行摘要（总线数 / 在线设备 / 隔离点 / 是否有故障）。</summary>
        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        /// <summary>空态或失败原因（用户看得见，不弹泡）。</summary>
        public string Hint
        {
            get => _hint;
            private set => SetProperty(ref _hint, value);
        }

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() => _timer.Stop();

        public void OnDialogOpened(IDialogParameters parameters)
        {
            SafeRefresh();
            _timer.Start();
        }

        /// <summary>
        /// 供系统健康壳调用：刷新一拍。
        /// <para>壳不调用 <c>OnDialogOpened</c>（那会启动本面板自己的定时器），
        /// 只按自己的节拍调这里——一个弹窗只允许有一个定时器。</para>
        /// </summary>
        public void Refresh() => SafeRefresh();

        /// <summary>
        /// 刷新一拍。异常必须就地兜住：<c>DispatcherTimer.Tick</c> 的异常没有兜底，
        /// 会一路冒到 Dispatcher 把整个软件打崩，而这里只是"每秒看一眼诊断数据"。
        /// </summary>
        private void SafeRefresh()
        {
            try
            {
                Apply(_monitor.Capture());
            }
            catch (Exception ex)
            {
                _timer.Stop();
                HasData = false;
                Hint = $"诊断数据读取失败，已停止自动刷新：{ex.Message}";
                Summary = "采集失败";
            }
        }

        private void Apply(IoHealthSnapshot snapshot)
        {
            Summary = snapshot.Summary;
            Hint = snapshot.TotalDevices == 0 ? "当前方案没有配置 IO 总线或设备" : string.Empty;
            HasData = snapshot.TotalDevices > 0;

            SyncRows(Buses, snapshot.Buses, b => b.BusId, (row, item) => row.Update(item),
                item => new IoBusHealthRow(item));
            SyncRows(Devices, snapshot.Devices, d => d.DeviceId, (row, item) => row.Update(item),
                item => new IoDeviceHealthRow(item));
            SyncRows(Points, snapshot.Points, p => $"{p.DeviceId}.{p.PointId}",
                (row, item) => row.Update(item), item => new IoPointHealthRow(item));

            RefreshAudit();
        }

        /// <summary>
        /// 按稳定键原位对齐：键相同则更新（保住选中与滚动），键消失则移除，键新增则插入。
        /// 直接 Clear+Add 会让 1s 一次的刷新把用户正在看的行位置每秒重置一次。
        /// </summary>
        private static void SyncRows<TRow, TItem>(
            ObservableCollection<TRow> target,
            IReadOnlyList<TItem> source,
            Func<TItem, string> keyOf,
            Action<TRow, TItem> update,
            Func<TItem, TRow> create)
        {
            var existing = new Dictionary<string, TRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in target)
            {
                var key = KeyOf(row);
                if (key != null) existing[key] = row;
            }

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
                wanted.Add(keyOf(item));

            for (int i = target.Count - 1; i >= 0; i--)
            {
                var key = KeyOf(target[i]);
                if (key == null || !wanted.Contains(key))
                    target.RemoveAt(i);
            }

            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                var key = keyOf(item);
                if (existing.TryGetValue(key, out var row))
                {
                    update(row, item);
                    int current = target.IndexOf(row);
                    if (current >= 0 && current != i)
                        target.Move(current, Math.Min(i, target.Count - 1));
                }
                else
                {
                    target.Insert(Math.Min(i, target.Count), create(item));
                }
            }

            string? KeyOf(TRow row) => row switch
            {
                IoBusHealthRow bus => bus.BusId,
                IoDeviceHealthRow device => device.DeviceId,
                IoPointHealthRow point => $"{point.DeviceId}.{point.PointId}",
                _ => null
            };
        }
    }
}
