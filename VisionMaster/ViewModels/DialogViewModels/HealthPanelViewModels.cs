using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VisionMaster.Services.Health;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>通讯面板。</summary>
    public sealed class CommunicationHealthPanelViewModel : HealthPanelViewModelBase
    {
        private readonly CommunicationHealthMonitor _monitor;
        private IReadOnlyList<HealthIssue> _issues = System.Array.Empty<HealthIssue>();

        public CommunicationHealthPanelViewModel(CommunicationHealthMonitor monitor)
            => _monitor = monitor;

        public override string Domain => "通讯";
        public override string Title => "通讯";

        public ObservableCollection<CommunicationHealthRow> Rows { get; } = new();

        protected override void RefreshCore()
        {
            var snapshot = _monitor.Capture();
            SyncRows(Rows, snapshot.Rows, r => r.ConnectionName);

            Summary = snapshot.Summary;
            Detail = snapshot.Total == 0 ? "当前方案没有配置通讯连接。" : string.Empty;

            _issues = snapshot.Rows
                .Where(r => r.Level is HealthLevel.Error or HealthLevel.Warn)
                .Select(r => new HealthIssue(Domain, r.ConnectionName, r.Level, r.DetailText, r.PollText))
                .ToList();

            ApplyIssues(_issues, snapshot.Total > 0);
        }

        protected override IReadOnlyList<HealthIssue> CollectIssuesCore() => _issues;
    }

    /// <summary>相机面板。</summary>
    public sealed class CameraHealthPanelViewModel : HealthPanelViewModelBase
    {
        private readonly CameraHealthMonitor _monitor;
        private IReadOnlyList<HealthIssue> _issues = System.Array.Empty<HealthIssue>();

        public CameraHealthPanelViewModel(CameraHealthMonitor monitor)
            => _monitor = monitor;

        public override string Domain => "相机";
        public override string Title => "相机";

        public ObservableCollection<CameraHealthRow> Rows { get; } = new();

        protected override void RefreshCore()
        {
            var snapshot = _monitor.Capture();
            SyncRows(Rows, snapshot.Rows, r => r.CameraId.ToString("N"));

            Summary = snapshot.Summary;
            Detail = snapshot.Total == 0
                ? "当前方案没有配置相机。"
                : "状态只证明「连接 / 已请求采流」；出图证据看「最近收帧」（本采样周期增量）。";

            _issues = snapshot.Rows
                .Where(r => r.Level is HealthLevel.Error or HealthLevel.Warn)
                .Select(r => new HealthIssue(Domain, r.DisplayName, r.Level, r.DetailText, r.LastFrameText))
                .ToList();

            ApplyIssues(_issues, snapshot.Total > 0);
        }

        protected override IReadOnlyList<HealthIssue> CollectIssuesCore() => _issues;
    }

    /// <summary>运动卡面板。</summary>
    public sealed class MotionHealthPanelViewModel : HealthPanelViewModelBase
    {
        private readonly MotionHealthMonitor _monitor;
        private IReadOnlyList<HealthIssue> _issues = System.Array.Empty<HealthIssue>();

        public MotionHealthPanelViewModel(MotionHealthMonitor monitor)
            => _monitor = monitor;

        public override string Domain => "运动";
        public override string Title => "运动";

        public ObservableCollection<MotionHealthRow> Rows { get; } = new();

        protected override void RefreshCore()
        {
            var snapshot = _monitor.Capture();
            SyncRows(Rows, snapshot.Rows, r => r.CardId.ToString("N"));

            Summary = snapshot.Summary;
            Detail = snapshot.Total == 0
                ? "当前方案没有配置运动卡。"
                : "等级只看当前状态与轴快照；「最近故障」是历史记录，不会自动清除。";

            _issues = snapshot.Rows
                .Where(r => r.Level is HealthLevel.Error or HealthLevel.Warn)
                .Select(r => new HealthIssue(Domain, r.Caption, r.Level, r.DetailText, r.StateText))
                .ToList();

            ApplyIssues(_issues, snapshot.Total > 0);
        }

        protected override IReadOnlyList<HealthIssue> CollectIssuesCore() => _issues;
    }
}
